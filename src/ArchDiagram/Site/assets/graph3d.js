/* ArchDiagram 3D dependency graph controller.
   Renders graph.json with the vendored ForceGraph3D bundle. Five data channels:
   position (force layout) · colour (folder) · size (fan-in+fan-out) · edge style
   (import solid / call dashed) · focus-distance (click-to-unfold ego animation).
   Fully offline: reads the local graph.json only. Degrades to an empty-state
   when WebGL or the bundle is unavailable. */
(function () {
  "use strict";
  var root = document.getElementById("graph3d-root");
  var canvas = document.getElementById("graph3d-canvas");
  if (!root || !canvas) { return; }

  function fail(msg) {
    root.innerHTML = '<div class="panel empty-state"><div class="big">🕸</div><p>' + msg +
      ' Use the <a href="dependencies.html">Dependencies</a> page for the 2D view.</p></div>';
  }

  if (typeof ForceGraph3D !== "function") {
    fail("The 3D graph engine could not be loaded.");
    return;
  }
  // WebGL capability probe.
  try {
    var probe = document.createElement("canvas");
    if (!(probe.getContext("webgl") || probe.getContext("experimental-webgl"))) {
      fail("3D graph needs WebGL, which this browser/session doesn't provide.");
      return;
    }
  } catch (e) {
    fail("3D graph needs WebGL, which this browser/session doesn't provide.");
    return;
  }

  function cssVar(name) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  }
  function isDark() { return document.documentElement.getAttribute("data-theme") === "dark"; }

  // Deterministic folder -> colour so reloads look identical.
  var palette = ["#2f6fab", "#b7791f", "#2e7d32", "#6b46c1", "#c0392b", "#1f8a8a",
    "#e8b73a", "#7a5195", "#ef5675", "#3572A5", "#488f31", "#0060ac"];
  var folderColor = {};
  function colorForFolder(f) {
    if (!(f in folderColor)) {
      var h = 0;
      for (var i = 0; i < f.length; i++) { h = (h * 31 + f.charCodeAt(i)) & 0x7fffffff; }
      folderColor[f] = palette[h % palette.length];
    }
    return folderColor[f];
  }

  // Language -> colour. Mirrors IndexPage.LangColors (src/ArchDiagram/Site/Pages/IndexPage.cs)
  // so the graph's "colour by file type" matches the Overview language bar.
  var LANG_COLORS = {
    "C#": "#2f6fab", "TypeScript/JavaScript": "#e8b73a", "Python": "#3572A5",
    "PowerShell": "#6b46c1", "SQL": "#c0392b", "HTML": "#e34c26", "CSS": "#563d7c",
    "JSON": "#8a8a8a", "YAML": "#6fbf73", "XML": "#0060ac", "Markdown": "#4a4a4a",
    "MSBuild": "#68217a", "Razor": "#512bd4", "Protobuf": "#4d7e65"
  };
  var langFallback = ["#1f8a8a", "#b7791f", "#7a5195", "#ef5675", "#488f31"];
  var langColorCache = {};
  function colorForLang(lang) {
    if (LANG_COLORS[lang]) { return LANG_COLORS[lang]; }
    if (!(lang in langColorCache)) {
      var h = 0, s = String(lang || "");
      for (var i = 0; i < s.length; i++) { h = (h * 31 + s.charCodeAt(i)) & 0x7fffffff; }
      langColorCache[lang] = langFallback[h % langFallback.length];
    }
    return langColorCache[lang];
  }

  // Blend a #rrggbb colour toward grey — used to mute test-file nodes so they read
  // as support code without stealing a colour slot. (The vendored bundle does not
  // expose THREE, so per-node geometry isn't available; muting is the offline-safe
  // signal. A box shape auto-activates if window.THREE ever becomes reachable.)
  function muteHex(hex) {
    if (!/^#[0-9a-fA-F]{6}$/.test(hex)) { return hex; }
    var r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
    var grey = 128, f = 0.6; // 60% toward mid-grey
    r = Math.round(r + (grey - r) * f); g = Math.round(g + (grey - g) * f); b = Math.round(b + (grey - b) * f);
    return "#" + [r, g, b].map(function (v) { return ("0" + v.toString(16)).slice(-2); }).join("");
  }

  var hopsEl = document.getElementById("g3d-hops");
  var hopsValEl = document.getElementById("g3d-hops-val");
  var spreadEl = document.getElementById("g3d-spread");
  var spreadValEl = document.getElementById("g3d-spread-val");
  var callsEl = document.getElementById("g3d-calls");
  var importsEl = document.getElementById("g3d-imports");
  var filterEl = document.getElementById("g3d-filter");
  var resetEl = document.getElementById("g3d-reset");
  var countEl = document.getElementById("g3d-count");
  var panel = document.getElementById("graph3d-panel");
  var hideTestsEl = document.getElementById("g3d-hide-tests");
  var colorEl = document.getElementById("g3d-color");
  var degreeModeEl = document.getElementById("g3d-degree-mode");
  var degreeNEl = document.getElementById("g3d-degree-n");
  var searchEl = document.getElementById("g3d-search");
  var searchListEl = document.getElementById("g3d-search-list");
  var isolateEl = document.getElementById("g3d-isolate");
  var degreeHideEl = document.getElementById("g3d-degree-hide");
  var hideOrphansEl = document.getElementById("g3d-hide-orphans");
  var freezeEl = document.getElementById("g3d-freeze");
  var typeLegendEl = document.getElementById("g3d-type-legend");
  var pathEl = document.getElementById("g3d-path");
  var pathPlayEl = document.getElementById("g3d-path-play");

  var colorMode = "coupling";     // "coupling" (default) | "folder" | "type"
  var degreeMode = "off";          // "off" | "ge" | "le"
  var degreeN = 5;
  var frozen = false;
  var filterText = "";             // transient highlight filter (path / folder / language substring)
  var didInitialFit = false;       // auto-fit the camera only once; never yank it mid-interaction
  var maxDegree = 1;               // set from data in init(), drives the coupling heat scale
  function degreeOf(n) { return (n.fanIn || 0) + (n.fanOut || 0); }
  // A node passes the highlight filter (only meaningful when degreeMode !== "off").
  function degreeHit(n) {
    if (degreeMode === "ge") { return degreeOf(n) >= degreeN; }
    if (degreeMode === "le") { return degreeOf(n) <= degreeN; }
    return true;
  }
  function isolateActive() { return !!(isolateEl && isolateEl.checked && focusId); }
  function inEgo(n) { return n.id === focusId || n.__ego; }
  // A node matches the highlight filter (path / folder / language substring; empty = all match).
  function filterHit(n) {
    if (!filterText) { return true; }
    return (n.path && n.path.toLowerCase().indexOf(filterText) >= 0) ||
           (n.folder && n.folder.toLowerCase().indexOf(filterText) >= 0) ||
           (n.lang && n.lang.toLowerCase().indexOf(filterText) >= 0);
  }

  // ---- Control-state persistence (localStorage; guarded for file:// where it may throw). ----
  var STORE_KEY = "archdiagram-graph3d";
  function loadPrefs() {
    try { return JSON.parse(localStorage.getItem(STORE_KEY) || "{}") || {}; } catch (e) { return {}; }
  }
  function savePrefs() {
    try {
      localStorage.setItem(STORE_KEY, JSON.stringify({
        spread: spreadEl ? spreadEl.value : null,
        color: colorMode,
        hideTests: !!(hideTestsEl && hideTestsEl.checked),
        showCalls: !callsEl || callsEl.checked,
        showImports: !importsEl || importsEl.checked,
        degreeMode: degreeMode, degreeN: degreeN,
        degreeHide: !!(degreeHideEl && degreeHideEl.checked),
        isolate: !!(isolateEl && isolateEl.checked),
        hideOrphans: !!(hideOrphansEl && hideOrphansEl.checked)
      }));
    } catch (e) { /* ignore (private mode / file://) */ }
  }

  // When colouring by file type, show a swatch row of the languages actually present so
  // the colours are decodable. Capped; the overflow is reported, never silently dropped.
  var TYPE_LEGEND_CAP = 16;
  function updateTypeLegend() {
    if (!typeLegendEl) { return; }
    if (colorMode !== "type") { typeLegendEl.hidden = true; typeLegendEl.innerHTML = ""; return; }
    var seen = {}, langs = [];
    DATA.nodes.forEach(function (n) { var l = n.lang || "?"; if (!seen[l]) { seen[l] = 1; langs.push(l); } });
    langs.sort();
    var shown = langs.slice(0, TYPE_LEGEND_CAP);
    var html = "<strong>File types</strong> ";
    html += shown.map(function (l) {
      return '<span class="legend-swatch" style="background:' + colorForLang(l) +
        ';border-color:' + colorForLang(l) + '"></span>' + esc(l);
    }).join(" &nbsp; ");
    if (langs.length > shown.length) { html += " &nbsp; +" + (langs.length - shown.length) + " more"; }
    typeLegendEl.innerHTML = html;
    typeLegendEl.hidden = false;
  }

  var MUTED = function () { return cssVar("--text-soft") || "#888"; };
  var ACCENT = function () { return cssVar("--accent") || "#2f6fab"; };

  var Graph = null, DATA = { nodes: [], links: [] }, ADJ = {}, focusId = null;
  var PATH = null, playing = false;   // critical-path highlight state
  function pathLinkHit(l) {
    if (!PATH) { return false; }
    var s = typeof l.source === "object" ? l.source.id : l.source;
    var t = typeof l.target === "object" ? l.target.id : l.target;
    return PATH.edges[s + ">" + t] === true;
  }
  var lastGraphSig = null;          // visible node-set signature; skips spurious reheats

  function buildAdjacency(links) {
    ADJ = {};
    links.forEach(function (l) {
      var s = typeof l.source === "object" ? l.source.id : l.source;
      var t = typeof l.target === "object" ? l.target.id : l.target;
      (ADJ[s] = ADJ[s] || []).push(t);
      (ADJ[t] = ADJ[t] || []).push(s);
    });
  }

  // BFS ego set up to `hops` from a start node. Returns {id: distance}.
  function egoSet(startId, hops) {
    var dist = {}, queue = [startId];
    dist[startId] = 0;
    while (queue.length) {
      var cur = queue.shift();
      if (dist[cur] >= hops) { continue; }
      (ADJ[cur] || []).forEach(function (n) {
        if (!(n in dist)) { dist[n] = dist[cur] + 1; queue.push(n); }
      });
    }
    return dist;
  }

  // Cool -> hot coupling scale: low degree = blue, high = red (interpolated in RGB).
  function colorForCoupling(n) {
    var t = maxDegree > 0 ? Math.min(1, degreeOf(n) / maxDegree) : 0;
    var cool = [47, 111, 171], hot = [192, 57, 43]; // #2f6fab -> #c0392b
    var c = cool.map(function (v, i) { return Math.round(v + (hot[i] - v) * t); });
    return "#" + c.map(function (v) { return ("0" + v.toString(16)).slice(-2); }).join("");
  }

  // Base colour by the active channel, then mute test files so they read as support code.
  function baseColor(n) {
    var c = colorMode === "coupling" ? colorForCoupling(n)
          : colorMode === "type" ? colorForLang(n.lang)
          : colorForFolder(n.folder);
    return n.test ? muteHex(c) : c;
  }
  // Colour precedence: focus dimming (when a node is focused) wins over the degree
  // highlight, which in turn overrides the plain base colour. Matches the legend.
  function nodeColor(n) {
    if (PATH) {
      if (!PATH.ids[n.id]) { return MUTED(); }
      return n.id === PATH.target ? (cssVar("--danger") || "#c0392b") : ACCENT();
    }
    if (focusId) {
      if (n.id === focusId) { return ACCENT(); }
      return n.__ego ? baseColor(n) : MUTED();
    }
    if (filterText && !filterHit(n)) { return MUTED(); }
    if (degreeMode !== "off") { return degreeHit(n) ? baseColor(n) : MUTED(); }
    return baseColor(n);
  }
  function nodeVal(n) {
    var base = 1 + (n.fanIn || 0) + (n.fanOut || 0);
    if (focusId && (n.id === focusId)) { return base * 2.2; }
    if (focusId && n.__ego) { return base * 1.3; }
    if (!focusId && degreeMode !== "off" && degreeHit(n)) { return base * 1.6; }
    return base;
  }
  function linkColor(l) {
    if (PATH) { return pathLinkHit(l) ? (cssVar("--danger") || "#c0392b") : MUTED(); }
    if (filterText) {
      var s = typeof l.source === "object" ? l.source : null;
      var t = typeof l.target === "object" ? l.target : null;
      if (!((s && filterHit(s)) || (t && filterHit(t)))) { return MUTED(); }
    }
    return l.kind === "call" ? (cssVar("--warn") || "#b7791f") : ACCENT();
  }
  function linkWidth(l) { return PATH && pathLinkHit(l) ? 3 : 1; }
  function linkParticles(l) { return playing && pathLinkHit(l) ? 4 : 0; }

  function applyChannels() {
    if (!Graph) { return; }
    Graph.nodeColor(nodeColor).nodeVal(nodeVal).linkColor(linkColor)
      .linkWidth(linkWidth).linkDirectionalParticles(linkParticles).linkDirectionalParticleWidth(2.5);
  }

  function setFocus(id) {
    focusId = id;
    var node = DATA.nodes.find(function (n) { return n.id === id; });
    if (!node) { return; }
    var hops = +(hopsEl && hopsEl.value || 2);
    var dist = egoSet(id, hops);
    DATA.nodes.forEach(function (n) { n.__ego = (n.id in dist) && n.id !== id; });
    // Isolate mode hides everything outside the ego set; otherwise just recolour.
    if (isolateActive()) { applyGraphData(); } else { applyChannels(); }
    // Fly the camera to look at the focused node from a modest distance.
    var d = 120;
    var p = node;
    if (typeof p.x === "number") {
      var dist0 = Math.hypot(p.x, p.y, p.z || 0);
      var ratio = dist0 > 1 ? 1 + d / dist0 : 1; // guard against a node at/near the origin
      Graph.cameraPosition({ x: p.x * ratio, y: p.y * ratio, z: (p.z || 0) * ratio + d },
        { x: p.x, y: p.y, z: p.z || 0 }, 800);
    }
    showPanel(node, dist);
  }

  function clearFocus() {
    if (PATH) {
      PATH = null; playing = false;
      if (pathEl) { pathEl.value = ""; }
      if (pathPlayEl) { pathPlayEl.disabled = true; pathPlayEl.textContent = "▶ Play"; }
    }
    var wasIsolated = isolateActive();
    focusId = null;
    DATA.nodes.forEach(function (n) { n.__ego = false; });
    // Restore the full node set if isolate had hidden non-ego nodes; else just recolour.
    if (wasIsolated) { applyGraphData(); } else { applyChannels(); }
    if (panel) { panel.hidden = true; }
  }

  // ---- Critical paths: trace + optional pulse animation ----
  function populatePathList() {
    if (!pathEl) { return; }
    var paths = DATA.criticalPaths || [];
    if (!paths.length) { pathEl.parentNode && (pathEl.closest(".lf-select").style.display = "none"); return; }
    var html = '<option value="">— none —</option>';
    for (var i = 0; i < paths.length; i++) {
      html += '<option value="' + i + '">' + esc(paths[i].label) + '</option>';
    }
    pathEl.innerHTML = html;
  }

  function clearPath() { clearFocus(); }

  function selectPath(idx) {
    var paths = DATA.criticalPaths || [];
    var p = paths[idx];
    if (!p) { clearPath(); return; }
    var byId = {}; DATA.nodes.forEach(function (n) { byId[n.id] = n; });
    PATH = { target: p.target, ids: {}, edges: {}, order: p.nodes };
    for (var i = 0; i < p.nodes.length; i++) {
      PATH.ids[p.nodes[i]] = true;
      if (i > 0) { PATH.edges[p.nodes[i - 1] + ">" + p.nodes[i]] = true; }
    }
    playing = false;
    if (pathPlayEl) { pathPlayEl.disabled = false; pathPlayEl.textContent = "▶ Play"; }
    // Reuse focus for the camera fly-in + ego layout, then our PATH styling overrides colour.
    focusId = p.target;
    DATA.nodes.forEach(function (n) { n.__ego = false; });
    applyChannels();
    var tgt = byId[p.target];
    if (tgt && typeof tgt.x === "number") {
      var d = 140, dist0 = Math.hypot(tgt.x, tgt.y, tgt.z || 0), ratio = dist0 > 1 ? 1 + d / dist0 : 1;
      Graph.cameraPosition({ x: tgt.x * ratio, y: tgt.y * ratio, z: (tgt.z || 0) * ratio + d },
        { x: tgt.x, y: tgt.y, z: tgt.z || 0 }, 800);
    }
    showPathPanel(p, byId);
  }

  // Side panel: the chain, hop-by-hop, with cumulative coupling transferred along the way.
  function showPathPanel(p, byId) {
    if (!panel) { return; }
    var cum = 0;
    var rows = p.nodes.map(function (id, i) {
      var n = byId[id] || { label: id, fanIn: 0, fanOut: 0 };
      cum += (n.fanIn || 0) + (n.fanOut || 0);
      var arrow = i === 0 ? '<span class="badge">entry</span> ' : '<span class="crumb-sep">→</span> ';
      return '<li>' + arrow + esc(n.label) +
        ' <span class="filter-count">coupling ' + ((n.fanIn || 0) + (n.fanOut || 0)) +
        ' · Σ ' + cum + '</span></li>';
    }).join("");
    panel.innerHTML =
      '<h3 style="margin-top:0">Critical path</h3>' +
      '<p class="note">Entry point → <strong>' + esc(p.label) + '</strong> · ' + (p.nodes.length - 1) + ' hop(s) · ' +
      'total coupling along the path <strong>' + cum + '</strong>.</p>' +
      '<ul class="member-list" style="font-family:inherit">' + rows + '</ul>' +
      '<p class="note">Press <strong>Play</strong> to pulse the flow. Pick “— none —” to exit.</p>';
    panel.hidden = false;
  }

  function togglePlay() {
    if (!PATH) { return; }
    playing = !playing;
    if (pathPlayEl) { pathPlayEl.textContent = playing ? "⏸ Pause" : "▶ Play"; }
    applyChannels();
  }

  function sourceControl(node) {
    if (window.ArchSourceLink && window.ArchSourceLink.configured()) {
      var u = window.ArchSourceLink.url(node.path, 0);
      if (u) { return '<a class="btn" href="' + u + '" target="_blank" rel="noopener">Open source ↗</a>'; }
    }
    return '<button class="btn" type="button" id="g3d-setsource">Set source link…</button>';
  }

  function showPanel(node, dist) {
    if (!panel) { return; }
    var neighbours = (ADJ[node.id] || []).filter(function (v, i, a) { return a.indexOf(v) === i; });
    var byId = {};
    DATA.nodes.forEach(function (n) { byId[n.id] = n; });
    var items = neighbours.slice(0, 40).map(function (nid) {
      var nn = byId[nid];
      return '<li data-id="' + nid + '">' + esc(nn ? nn.label : nid) + '</li>';
    }).join("");
    panel.hidden = false;
    panel.innerHTML =
      '<div style="display:flex;justify-content:space-between;align-items:baseline;gap:.5rem">' +
      '<strong style="font-size:.95rem">' + esc(node.label) + '</strong>' +
      '<button class="btn" id="g3d-panel-close" type="button" title="Close">✕</button></div>' +
      '<p style="margin:.3rem 0"><code>' + esc(node.path) + '</code></p>' +
      '<p style="margin:.3rem 0">' +
      '<span class="badge">' + esc(node.lang) + '</span> ' +
      '<span class="badge">' + (node.loc || 0) + ' LOC</span> ' +
      '<span class="badge">in ' + (node.fanIn || 0) + '</span> ' +
      '<span class="badge">out ' + (node.fanOut || 0) + '</span></p>' +
      '<p style="margin:.5rem 0;display:flex;gap:.4rem;flex-wrap:wrap">' +
      '<a class="btn" href="' + node.href + '">Open file page →</a>' + sourceControl(node) + '</p>' +
      '<p class="note" style="margin:.4rem 0 .2rem">Neighbours (' + neighbours.length + ') — click to refocus</p>' +
      '<ul class="graph3d-neighbours">' + items + '</ul>';

    panel.querySelector("#g3d-panel-close").onclick = clearFocus;
    var setBtn = panel.querySelector("#g3d-setsource");
    if (setBtn && window.ArchSourceLink) { setBtn.onclick = function () { window.ArchSourceLink.prompt(); }; }
    panel.querySelectorAll(".graph3d-neighbours li").forEach(function (li) {
      li.onclick = function () { setFocus(li.getAttribute("data-id")); };
    });
  }

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function loadingNote(text) {
    var n = document.createElement("div");
    n.className = "note";
    n.id = "g3d-loading";
    n.textContent = text;
    n.style.position = "absolute";
    n.style.top = ".75rem";
    n.style.left = ".75rem";
    n.style.zIndex = "5";
    root.appendChild(n);
    return n;
  }

  // Bigger graphs clump at a fixed spread, so open them a little looser by default.
  // Kept modest: the camera refits after each layout so spread never pushes nodes
  // out of frame (see fitView / onEngineStop).
  function defaultSpread(count) {
    return count < 50 ? 2 : count < 200 ? 3 : count < 600 ? 4 : 5;
  }

  // Frame the whole graph. Force-graph only auto-positions the camera once at start,
  // so after the layout spreads we must refit or the nodes drift out of view (this
  // was the "blank graph" regression). Guarded; no-op until there are laid-out nodes.
  function fitView(ms) {
    if (!Graph || !Graph.zoomToFit || !DATA.nodes.length) { return; }
    var n = DATA.nodes[0];
    // Only fit once the simulation has assigned real coordinates; fitting a graph
    // whose nodes are still at undefined/NaN positions throws the camera to nowhere.
    if (typeof n.x !== "number" || !isFinite(n.x)) { return; }
    // Frame the CONNECTED core (degree > 0) when one exists, so a few far-flung orphans
    // can't force a huge zoom-out (that was the "Reset zooms too far" report). Falls back
    // to all nodes if everything is an orphan. zoomToFit(duration, padding, nodeFilterFn).
    var connected = DATA.nodes.some(function (d) { return degreeOf(d) > 0; });
    var filter = connected ? function (d) { return degreeOf(d) > 0; } : function () { return true; };
    try { Graph.zoomToFit(ms == null ? 400 : ms, 60, filter); } catch (e) { /* engine not ready */ }
  }

  // Restore saved control state (or sensible defaults) BEFORE the first render so the
  // graph opens the way the user left it. Sets element values and the state mirrors.
  function restorePrefs() {
    var p = loadPrefs();
    if (spreadEl) {
      spreadEl.value = p.spread != null ? p.spread : defaultSpread(DATA.nodes.length);
      if (spreadValEl) { spreadValEl.textContent = spreadEl.value; }
    }
    if (colorEl && p.color) { colorEl.value = p.color; }
    colorMode = colorEl ? colorEl.value : "coupling";
    if (hideTestsEl) {
      // Honour a per-graph choice if the user made one; otherwise follow the site-wide
      // default (tests hidden unless the global 🧪 toggle turned them on).
      if (typeof p.hideTests === "boolean") { hideTestsEl.checked = p.hideTests; }
      else {
        var showTests = null;
        try { showTests = localStorage.getItem("archdiagram-show-tests"); } catch (e) { }
        hideTestsEl.checked = showTests !== "1";
      }
    }
    if (callsEl && typeof p.showCalls === "boolean") { callsEl.checked = p.showCalls; }
    if (importsEl && typeof p.showImports === "boolean") { importsEl.checked = p.showImports; }
    if (degreeModeEl && p.degreeMode) { degreeModeEl.value = p.degreeMode; }
    degreeMode = degreeModeEl ? degreeModeEl.value : "off";
    if (degreeNEl) {
      if (p.degreeN != null) { degreeNEl.value = p.degreeN; }
      degreeNEl.disabled = degreeMode === "off";
      degreeN = parseInt(degreeNEl.value, 10) || 0;
    }
    if (degreeHideEl && typeof p.degreeHide === "boolean") { degreeHideEl.checked = p.degreeHide; }
    if (isolateEl && typeof p.isolate === "boolean") { isolateEl.checked = p.isolate; }
    if (hideOrphansEl && typeof p.hideOrphans === "boolean") { hideOrphansEl.checked = p.hideOrphans; }
  }

  // Deep link: graph.html#path=<relpath> (or #focus=<slug>) auto-focuses on load, so
  // file pages and search results can link straight into the graph.
  function applyDeepLink() {
    var h = (location.hash || "").replace(/^#/, "");
    var m = /(?:^|&)(?:path|focus)=([^&]+)/.exec(h);
    if (!m) { return; }
    var val = decodeURIComponent(m[1]);
    var node = DATA.nodes.find(function (n) { return n.path === val || n.id === val; });
    if (!node) { return; }
    if (node.test && hideTestsEl && hideTestsEl.checked) { hideTestsEl.checked = false; applyGraphData(); }
    // Defer until the layout has assigned coordinates so the camera fly-to works.
    setTimeout(function () { setFocus(node.id); }, 700);
  }

  function init(data) {
    DATA = { nodes: data.nodes || [], links: data.edges || [], criticalPaths: data.criticalPaths || [] };
    buildAdjacency(DATA.links);
    maxDegree = DATA.nodes.reduce(function (m, n) { return Math.max(m, degreeOf(n)); }, 1);
    restorePrefs();
    if (countEl) {
      countEl.textContent = data.shownNodes < data.totalFiles
        ? "showing " + data.shownNodes + " of " + data.totalFiles + " files"
        : data.shownNodes + " files";
    }
    var loading = loadingNote("Laying out " + DATA.nodes.length + " files…");

    Graph = ForceGraph3D()(canvas)
      .backgroundColor(cssVar("--diagram-bg") || (isDark() ? "#151a21" : "#ffffff"))
      .nodeLabel(function (n) { return n.path + (n.test ? " · test" : ""); })
      .nodeOpacity(0.95)
      .linkOpacity(0.5)
      .linkDirectionalArrowLength(3)
      .linkDirectionalArrowRelPos(1)
      // Pre-run the simulation off-screen so the graph is already spread on first
      // paint (no "explode from the origin" animation), then cool quickly and quietly.
      // Warmup is capped so large graphs don't freeze the tab (it runs synchronously).
      .warmupTicks(Math.min(120, 20 + Math.round((DATA.nodes.length || 0) / 4)))
      .cooldownTicks(60)
      .cooldownTime(4000)
      .onNodeClick(function (n) { setFocus(n.id); })
      .onBackgroundClick(clearFocus)
      .graphData(DATA);

    // Box geometry for test files IF a future bundle exposes THREE; otherwise the
    // muteHex() tinting in baseColor() is the offline-safe test-file signal.
    if (window.THREE && typeof window.THREE.Mesh === "function") {
      var T = window.THREE;
      Graph.nodeThreeObject(function (n) {
        if (!n.test) { return null; } // null => default sphere
        var s = 2 + Math.cbrt(nodeVal(n)) * 2;
        var mesh = new T.Mesh(new T.BoxGeometry(s, s, s),
          new T.MeshLambertMaterial({ color: nodeColor(n) }));
        return mesh;
      });
    }

    populateSearchList();
    populatePathList();
    if (pathEl) {
      pathEl.addEventListener("change", function () {
        var v = pathEl.value;
        if (v === "") { clearPath(); } else { selectPath(+v); }
      });
    }
    if (pathPlayEl) { pathPlayEl.addEventListener("click", togglePlay); }
    applyChannels();
    lastGraphSig = null; // force the first data push
    applyGraphData();
    updateTypeLegend();
    applySpread(false); // set forces only; the engine runs its own first layout
    applyDeepLink();

    Graph.onEngineStop(function () {
      if (loading && loading.parentNode) { loading.parentNode.removeChild(loading); }
      // Frame the layout ONCE on first settle. The simulation re-cools after drags and
      // other reheats; fitting on every stop would keep yanking the camera to full zoom.
      if (!didInitialFit && !focusId) { fitView(500); didInitialFit = true; }
    });

    function resize() {
      Graph.width(canvas.clientWidth).height(canvas.clientHeight);
    }
    resize();
    window.addEventListener("resize", resize);

    // Fallback: clear the loading note even if the engine never fires onEngineStop.
    setTimeout(function () { if (loading && loading.parentNode) { loading.parentNode.removeChild(loading); } }, 8000);
  }

  // Push node+link data to the engine, applying the call-edge and hide-tests filters.
  // Adjacency (used by focus/ego + the neighbours panel) always reflects the FULL graph.
  function applyGraphData() {
    if (!Graph) { return; }
    var showCalls = !callsEl || callsEl.checked;
    var showImports = !importsEl || importsEl.checked;
    var hideTests = !!(hideTestsEl && hideTestsEl.checked);
    // If the focused node is about to be hidden, clear focus first so the panel
    // never references a node that is no longer in the scene.
    if (hideTests && focusId) {
      var fn = DATA.nodes.find(function (n) { return n.id === focusId; });
      if (fn && fn.test) { clearFocus(); }
    }
    var iso = isolateActive();
    var degreeHide = !!(degreeHideEl && degreeHideEl.checked);
    var hideOrphans = !!(hideOrphansEl && hideOrphansEl.checked);
    var nodes = DATA.nodes.filter(function (n) {
      if (hideTests && n.test) { return false; }
      if (iso && !inEgo(n)) { return false; }
      if (hideOrphans && degreeOf(n) === 0) { return false; }
      // Degree "hide non-matches" only applies when highlighting and nothing is focused.
      if (!focusId && degreeMode !== "off" && degreeHide && !degreeHit(n)) { return false; }
      return true;
    });
    var live = {};
    nodes.forEach(function (n) { live[n.id] = 1; });
    var links = (DATA.links || []).filter(function (l) {
      var s = typeof l.source === "object" ? l.source.id : l.source; // engine mutates strings -> objects
      var t = typeof l.target === "object" ? l.target.id : l.target;
      var kindOk = l.kind === "call" ? showCalls : showImports;
      return kindOk && live[s] && live[t];
    });
    // Only re-push (which reheats the sim and jostles every node) when the visible set
    // actually changed; otherwise recolour in place. Signature = counts + node ids only
    // (never link endpoints — the engine mutates those strings into objects after render).
    var sig = nodes.length + "|" + links.length + "|" + nodes.map(function (n) { return n.id; }).join(",");
    if (sig === lastGraphSig) { applyChannels(); savePrefs(); return; }
    lastGraphSig = sig;
    Graph.graphData({ nodes: nodes, links: links });
    buildAdjacency(DATA.links);
    applyChannels();
    savePrefs();
  }

  // Freeze pins every node at its current position (orbit still works); thaw releases
  // and reheats so the layout re-settles. Uses d3 fx/fy/fz fixed-position handles.
  function setFrozen(on) {
    frozen = on;
    DATA.nodes.forEach(function (n) {
      if (on) { n.fx = n.x; n.fy = n.y; n.fz = n.z; }
      else { n.fx = null; n.fy = null; n.fz = null; }
    });
    if (freezeEl) { freezeEl.textContent = on ? "Thaw" : "Freeze"; }
    if (!on && Graph && Graph.d3ReheatSimulation) { Graph.d3ReheatSimulation(); }
  }

  // Layout "spread": scale the many-body repulsion and link rest-length so the
  // graph reads as a constellation (high) instead of a tight clump (low). Larger
  // graphs clump because the default charge is fixed regardless of node count —
  // this lets the viewer counteract that. Reheats the simulation to re-settle.
  // Set the layout forces from the spread slider. Only reheat on USER changes —
  // reheating during init (before the engine's simulation exists) crashes the
  // render loop ("Cannot read properties of undefined (reading 'tick')").
  function applySpread(reheat) {
    if (!Graph || !Graph.d3Force) { return; }
    var f = spreadEl ? +spreadEl.value : 3; // 1..12
    // Gentle curve: link rest-length carries most of the spread; charge repulsion
    // grows only modestly so the layout never explodes off-screen.
    var charge = Graph.d3Force("charge");
    if (charge && charge.strength) { charge.strength(-40 - f * 8); }
    var link = Graph.d3Force("link");
    if (link && link.distance) { link.distance(20 + f * 16); }
    if (reheat && Graph.d3ReheatSimulation) { Graph.d3ReheatSimulation(); }
  }

  // Fill the search datalist from the loaded nodes (capped for very large graphs).
  var SEARCH_CAP = 1000;
  function populateSearchList() {
    if (!searchListEl) { return; }
    var n = Math.min(DATA.nodes.length, SEARCH_CAP);
    var html = "";
    for (var i = 0; i < n; i++) { html += '<option value="' + esc(DATA.nodes[i].path) + '">'; }
    searchListEl.innerHTML = html;
    if (DATA.nodes.length > SEARCH_CAP && window.console) {
      console.info("ArchDiagram graph search: listing first " + SEARCH_CAP + " of " +
        DATA.nodes.length + " files; typing still matches any file.");
    }
  }

  // Resolve a query to a node: exact path, then case-insensitive path/label substring.
  function findNode(q) {
    q = (q || "").trim();
    if (!q) { return null; }
    var exact = DATA.nodes.find(function (n) { return n.path === q; });
    if (exact) { return exact; }
    var lq = q.toLowerCase();
    return DATA.nodes.find(function (n) {
      return (n.path && n.path.toLowerCase().indexOf(lq) >= 0) ||
             (n.label && n.label.toLowerCase().indexOf(lq) >= 0);
    }) || null;
  }

  function runSearch() {
    if (!searchEl) { return; }
    var node = findNode(searchEl.value);
    if (!node) { searchEl.setAttribute("aria-invalid", "true"); return; }
    searchEl.removeAttribute("aria-invalid");
    // If the target is a hidden test file, reveal it before focusing.
    if (node.test && hideTestsEl && hideTestsEl.checked) {
      hideTestsEl.checked = false;
      applyGraphData();
    }
    setFocus(node.id);
  }

  // ---- Controls ----
  if (hopsEl) {
    hopsEl.addEventListener("input", function () {
      if (hopsValEl) { hopsValEl.textContent = hopsEl.value; }
      if (focusId) { setFocus(focusId); }
    });
  }
  if (spreadEl) {
    spreadEl.addEventListener("input", function () {
      if (spreadValEl) { spreadValEl.textContent = spreadEl.value; }
      applySpread(true);
      savePrefs();
    });
  }
  if (callsEl) { callsEl.addEventListener("change", applyGraphData); }
  if (importsEl) { importsEl.addEventListener("change", applyGraphData); }
  if (filterEl) {
    // Pure recolour (no node-set change) so the simulation never reheats and the camera holds.
    filterEl.addEventListener("input", function () {
      filterText = filterEl.value.trim().toLowerCase();
      applyChannels();
    });
  }
  if (hideTestsEl) { hideTestsEl.addEventListener("change", applyGraphData); }
  if (colorEl) {
    colorEl.addEventListener("change", function () { colorMode = colorEl.value; applyChannels(); updateTypeLegend(); savePrefs(); });
  }
  if (degreeModeEl) {
    degreeModeEl.addEventListener("change", function () {
      degreeMode = degreeModeEl.value;
      if (degreeNEl) { degreeNEl.disabled = degreeMode === "off"; }
      applyGraphData(); // re-filter (degree-hide) + recolour + save
    });
  }
  if (degreeNEl) {
    degreeNEl.addEventListener("input", function () {
      var v = parseInt(degreeNEl.value, 10);
      degreeN = isNaN(v) ? 0 : Math.max(0, v);
      if (degreeMode === "off") { return; }
      // Node set only changes when we actually hide non-matches; otherwise it's a pure
      // recolour — recolour in place (no reheat, no camera jostle).
      var hides = !focusId && degreeHideEl && degreeHideEl.checked;
      if (hides) { applyGraphData(); } else { applyChannels(); savePrefs(); }
    });
  }
  if (degreeHideEl) { degreeHideEl.addEventListener("change", applyGraphData); }
  if (isolateEl) { isolateEl.addEventListener("change", applyGraphData); }
  if (hideOrphansEl) { hideOrphansEl.addEventListener("change", applyGraphData); }
  if (freezeEl) { freezeEl.addEventListener("click", function () { setFrozen(!frozen); }); }
  if (searchEl) {
    searchEl.addEventListener("change", runSearch);
    searchEl.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); runSearch(); } });
  }
  if (resetEl) { resetEl.onclick = function () { clearFocus(); fitView(500); }; }
  document.addEventListener("keydown", function (e) {
    // "/" jumps to the search box (unless already typing in a field).
    if (e.key === "/" && searchEl && document.activeElement !== searchEl &&
        !/^(input|textarea|select)$/i.test((e.target && e.target.tagName) || "")) {
      e.preventDefault(); searchEl.focus(); return;
    }
    if (e.key === "Escape" && focusId) { clearFocus(); }
  });

  // Theme changes: retint background + recolour muted nodes.
  new MutationObserver(function () {
    if (Graph) { Graph.backgroundColor(cssVar("--diagram-bg") || (isDark() ? "#151a21" : "#ffffff")); applyChannels(); }
  }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

  // Re-link the panel when the user configures a source in-browser.
  if (window.ArchSourceLink && window.ArchSourceLink.onChange) {
    window.ArchSourceLink.onChange(function () {
      if (focusId) { setFocus(focusId); }
    });
  }

  // ---- Load graph data (offline; no network) ----
  // Prefer the inline payload (works from file://); fall back to fetch for callers
  // that serve the folder over http and strip the inline copy.
  if (window.ARCH_GRAPH) {
    init(window.ARCH_GRAPH);
  } else {
    fetch("graph.json").then(function (r) { return r.json(); }).then(init).catch(function () {
      fail("Could not load the graph data.");
    });
  }
})();
