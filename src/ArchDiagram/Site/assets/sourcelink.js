/* ArchDiagram source linking — turns repo-relative paths into GitHub/GitLab/local
   URLs. Config comes from window.ARCH_SOURCELINK (baked at generation time) or,
   when absent, an in-browser prompt whose answer persists in localStorage.
   Mirrors SourceLink.UrlFor in C# — keep the two in lockstep. */
(function () {
  "use strict";
  var KEY = "archdiagram-sourcelink";

  function load() {
    if (window.ARCH_SOURCELINK && window.ARCH_SOURCELINK.type &&
        window.ARCH_SOURCELINK.type !== "none") {
      return window.ARCH_SOURCELINK;
    }
    try {
      var raw = localStorage.getItem(KEY);
      if (raw) { return JSON.parse(raw); }
    } catch (e) { /* ignore */ }
    return null;
  }

  var cfg = load();

  function url(relPath, line) {
    if (!cfg || !cfg.base || !relPath) { return ""; }
    var path = String(relPath).replace(/\\/g, "/").replace(/^\/+/, "")
      .split("/").map(encodeURIComponent).join("/");
    var b = String(cfg.base).replace(/\/+$/, "");
    var ref = encodeURIComponent(cfg.ref || "main");
    var anchor = line > 0 ? "#L" + line : "";
    switch (cfg.type) {
      case "github": return b + "/blob/" + ref + "/" + path + anchor;
      case "gitlab": return b + "/-/blob/" + ref + "/" + path + anchor;
      case "local":
        var base = /^file:/i.test(b) ? b : "file:///" + b.replace(/ /g, "%20");
        return base + "/" + path;
      default: return "";
    }
  }

  function configured() { return !!(cfg && cfg.base && cfg.type && cfg.type !== "none"); }

  var listeners = [];
  function onChange(fn) { listeners.push(fn); }
  function notify() { listeners.forEach(function (fn) { try { fn(); } catch (e) { } }); }

  // Minimal prompt dialog reusing the palette overlay styling.
  function promptForConfig() {
    var overlay = document.createElement("div");
    overlay.className = "palette-overlay";
    overlay.innerHTML =
      '<div class="palette" style="width:min(460px,92vw);padding:1rem 1.1rem">' +
      '<h2 style="margin:.2rem 0 .6rem;font-size:1.05rem">Set source link</h2>' +
      '<p class="note" style="margin:.2rem 0 .8rem">Link nodes and files back to their source. ' +
      'Stored only in this browser.</p>' +
      '<label class="lf-range" style="display:flex;gap:.5rem;margin:.4rem 0">Host' +
      '<select id="sl-type"><option value="github">GitHub</option>' +
      '<option value="gitlab">GitLab</option><option value="local">Local file://</option></select></label>' +
      '<input id="sl-base" class="filter-input" style="width:100%;margin:.4rem 0" ' +
      'placeholder="https://github.com/org/repo  or  C:/src/app">' +
      '<input id="sl-ref" class="filter-input" style="width:100%;margin:.4rem 0" ' +
      'placeholder="branch / tag (default: main)">' +
      '<div style="display:flex;gap:.5rem;justify-content:flex-end;margin-top:.6rem">' +
      '<button class="btn" id="sl-cancel" type="button">Cancel</button>' +
      '<button class="btn btn-primary" id="sl-save" type="button">Save</button></div></div>';
    document.body.appendChild(overlay);
    var typeEl = overlay.querySelector("#sl-type");
    var baseEl = overlay.querySelector("#sl-base");
    var refEl = overlay.querySelector("#sl-ref");
    if (cfg) { typeEl.value = cfg.type || "github"; baseEl.value = cfg.base || ""; refEl.value = cfg.ref || ""; }
    baseEl.focus();
    function close() { document.body.removeChild(overlay); }
    overlay.addEventListener("mousedown", function (e) { if (e.target === overlay) { close(); } });
    overlay.querySelector("#sl-cancel").onclick = close;
    overlay.querySelector("#sl-save").onclick = function () {
      var next = { type: typeEl.value, base: baseEl.value.trim(), ref: (refEl.value.trim() || "main") };
      if (!next.base) { baseEl.focus(); return; }
      cfg = next;
      try { localStorage.setItem(KEY, JSON.stringify(next)); } catch (e) { }
      close();
      notify();
    };
  }

  // Auto-wire placeholders: any element with data-sourcelink-path becomes an
  // "Open source ↗" anchor when configured, or a "Set source link…" button that
  // opens the prompt. Re-rendered whenever the config changes.
  function wirePlaceholders() {
    document.querySelectorAll("[data-sourcelink-path]").forEach(function (el) {
      var path = el.getAttribute("data-sourcelink-path");
      var line = +(el.getAttribute("data-sourcelink-line") || 0);
      var mini = el.hasAttribute("data-sourcelink-mini");
      if (configured()) {
        var u = url(path, line);
        if (!u) { el.innerHTML = ""; return; }
        el.innerHTML = mini
          ? '<a href="' + u + '" target="_blank" rel="noopener" title="Open source">↗</a>'
          : '<a class="btn" href="' + u + '" target="_blank" rel="noopener">Open source ↗</a>';
      } else if (!mini) {
        el.innerHTML = '<button class="btn" type="button">Set source link…</button>';
        el.querySelector("button").onclick = promptForConfig;
      } else {
        el.innerHTML = "";
      }
    });
  }

  onChange(wirePlaceholders);
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", wirePlaceholders);
  } else {
    wirePlaceholders();
  }

  window.ArchSourceLink = {
    url: url,
    configured: configured,
    prompt: promptForConfig,
    onChange: onChange,
  };
})();
