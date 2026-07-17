using System.Diagnostics;
using System.Globalization;
using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Reads git history for the scanned tree in a single <c>git log</c> pass and aggregates,
/// per file, how many commits touched it, how many distinct authors, who the principal author is,
/// and when it last changed. Joined against complexity, this powers the "crime-scene" churn
/// hotspot view and bus-factor ownership analysis.
///
/// Offline and non-fatal by design: if git is absent, the tree isn't a working copy, or the
/// command fails for any reason, <see cref="Analyze"/> returns an unavailable result and the rest
/// of the pipeline proceeds with the churn fields at their defaults. Nothing here can throw into
/// the caller. Deterministic: all outputs are counts / set-sizes / max-dates that don't depend on
/// commit iteration order.</summary>
public static class GitHistory
{
    /// <summary>Per-file aggregates. AuthorCounts is the distinct author set; PrincipalAuthor is
    /// the author with the most commits (ties broken by ordinal name for determinism).</summary>
    public sealed record FileChurn(int CommitCount, int AuthorCount, string PrincipalAuthor, string LastModified);

    public sealed record Result(GitInfo Info, IReadOnlyDictionary<string, FileChurn> Files)
    {
        public static readonly Result Unavailable =
            new(new GitInfo { Available = false }, new Dictionary<string, FileChurn>(StringComparer.OrdinalIgnoreCase));
    }

    // A single commit can, on a very large repo, touch thousands of files; the whole history is
    // one streamed pass so this is bounded by total history size, read once. No per-file spawns.
    private const int GitTimeoutMs = 60_000;

    public static Result Analyze(string sourcePath, List<string> diagnostics)
    {
        // Not a git working copy is the NORMAL case for a dropped-in folder or a --from-model
        // rebuild — expected, not a problem, so it's silent. Only genuinely surprising conditions
        // below (a repo whose `git log` fails, a shallow clone) earn a diagnostic.
        string? repoRoot = RunGit(sourcePath, "rev-parse --show-toplevel")?.Trim();
        if (string.IsNullOrEmpty(repoRoot)) { return Result.Unavailable; }

        var shallow = string.Equals(RunGit(sourcePath, "rev-parse --is-shallow-repository")?.Trim(), "true", StringComparison.Ordinal);

        // Git reports paths relative to the repo ROOT; the scanned tree may be a subdirectory, and
        // FileNode.RelPath is relative to the SCAN root — compute the prefix to strip so the two align.
        var scanFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath)).Replace('\\', '/');
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot)).Replace('\\', '/');
        var subPrefix = scanFull.Length > rootFull.Length && scanFull.StartsWith(rootFull + "/", StringComparison.OrdinalIgnoreCase)
            ? scanFull[(rootFull.Length + 1)..] + "/"
            : "";

        // One pass. --no-renames keeps each historical path as it was (a rename shows as delete+add),
        // which is the honest, deterministic reading for a churn heuristic. Tab-delimited header per
        // commit, then numstat lines "<added>\t<deleted>\t<path>" for the files it touched.
        var log = RunGit(sourcePath, "log --no-merges --no-renames --format=%x01%an%x09%aI --numstat");
        if (log is null)
        {
            diagnostics.Add("Git history could not be read (git log failed); churn/ownership analysis skipped.");
            return Result.Unavailable;
        }

        var commits = new Dictionary<string, int>(StringComparer.Ordinal);   // path -> commit count
        var authors = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal); // path -> (author -> commits)
        var lastDate = new Dictionary<string, string>(StringComparer.Ordinal); // path -> max ISO date (yyyy-MM-dd)
        var totalCommits = 0;

        var author = "";
        var date = "";
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { continue; }
            if (line[0] == '\x01')
            {
                // Commit header: \x01<author>\t<ISO date>
                var parts = line[1..].Split('\t');
                author = parts.Length > 0 ? parts[0] : "";
                date = parts.Length > 1 ? IsoDay(parts[1]) : "";
                totalCommits++;
                continue;
            }
            // numstat line: <added>\t<deleted>\t<path>  (added/deleted are "-" for binary files)
            var cols = line.Split('\t');
            if (cols.Length < 3) { continue; }
            var gitPath = cols[2];
            if (subPrefix.Length > 0)
            {
                if (!gitPath.StartsWith(subPrefix, StringComparison.OrdinalIgnoreCase)) { continue; }
                gitPath = gitPath[subPrefix.Length..];
            }

            commits[gitPath] = commits.GetValueOrDefault(gitPath) + 1;
            var byAuthor = authors.TryGetValue(gitPath, out var a) ? a : authors[gitPath] = new(StringComparer.Ordinal);
            if (author.Length > 0) { byAuthor[author] = byAuthor.GetValueOrDefault(author) + 1; }
            if (string.CompareOrdinal(date, lastDate.GetValueOrDefault(gitPath, "")) > 0) { lastDate[gitPath] = date; }
        }

        var files = new Dictionary<string, FileChurn>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, count) in commits)
        {
            var byAuthor = authors.GetValueOrDefault(path);
            var principal = byAuthor is null || byAuthor.Count == 0 ? ""
                : byAuthor.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            files[path] = new FileChurn(count, byAuthor?.Count ?? 0, principal, lastDate.GetValueOrDefault(path, ""));
        }

        if (shallow) { diagnostics.Add("Git repository is a shallow clone; churn counts undercount real history."); }
        return new Result(new GitInfo { Available = true, Shallow = shallow, TotalCommits = totalCommits }, files);
    }

    /// <summary>yyyy-MM-dd from a git author-date (ISO-8601 like 2026-07-17T14:03:11+01:00);
    /// the leading 10 chars are the date, or "" if the input is too short to be one.</summary>
    private static string IsoDay(string iso) => iso.Length >= 10 ? iso[..10] : "";

    /// <summary>Runs <c>git &lt;args&gt;</c> in <paramref name="workingDir"/> and returns stdout, or
    /// null on any failure (git missing, non-zero exit, timeout). Never throws.</summary>
    private static string? RunGit(string workingDir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi);
            if (proc is null) { return null; }
            var stdout = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(GitTimeoutMs)) { try { proc.Kill(true); } catch { /* best effort */ } return null; }
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null; // git not on PATH, or process could not start
        }
    }
}
