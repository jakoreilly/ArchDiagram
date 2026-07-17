/* ArchDiagram viewer — dependency-free except vendored mermaid.min.js.
   Pan/zoom + PNG/SVG export, lazy per-card rendering, hover tooltips, selector
   groups, theme-aware diagrams with live re-render, Ctrl+K search palette,
   and client-side filters for the structure tree and type listings. */
(function () {
  "use strict";

  function currentTheme() {
    return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
  }

  function initMermaid() {
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "loose",
      theme: currentTheme() === "dark" ? "dark" : "neutral",
      maxTextSize: 200000,
      maxEdges: 100000,
      flowchart: { htmlLabels: false }
    });
  }
  initMermaid();

  var seq = 0;
  var tipEl = document.getElementById("hover-tip");

  // Rich hover card shared by diagram nodes and the metrics scatter. Pointer + keyboard.
  function bindTip(node, text) {
    if (!text || !tipEl) { return; }
    function show(e) {
      tipEl.textContent = text; tipEl.hidden = false;
      var px = (e && e.clientX), py = (e && e.clientY);
      if (px == null) { var r = node.getBoundingClientRect(); px = r.left + r.width / 2; py = r.top; }
      tipEl.style.left = Math.min(px + 14, window.innerWidth - tipEl.offsetWidth - 8) + "px";
      tipEl.style.top = Math.min(py + 14, window.innerHeight - tipEl.offsetHeight - 8) + "px";
    }
    node.addEventListener("mousemove", show);
    node.addEventListener("focus", show);
    node.addEventListener("mouseleave", function () { tipEl.hidden = true; });
    node.addEventListener("blur", function () { tipEl.hidden = true; });
  }

  function renderCard(card) {
    if (card.dataset.rendered) { return; }
    card.dataset.rendered = "1";
    var src = card.querySelector(".mermaid-src");
    var target = card.querySelector(".render-target");
    if (!src || !target) { return; }

    mermaid.render("mmd-" + (++seq), src.textContent).then(function (out) {
      target.innerHTML = out.svg;
      setupCard(card);
    }).catch(function (err) {
      target.innerHTML = "<div class='diagram-error'>Diagram failed to render: " +
        String(err && err.message || err).replace(/</g, "&lt;") + "</div>";
    });
  }

  function setupCard(card) {
    var stage = card.querySelector(".stage");
    var svg = stage.querySelector("svg");
    if (!svg) { return; }

    // Re-renders (theme toggle) must not stack stage/window listeners.
    if (card._ac) { card._ac.abort(); }
    var ac = new AbortController();
    card._ac = ac;
    var on = function (el, ev, fn, opts) {
      var o = opts || {};
      o.signal = ac.signal;
      el.addEventListener(ev, fn, o);
    };

    svg.removeAttribute("width");
    svg.removeAttribute("height");
    svg.style.width = "auto";
    svg.style.height = "auto";

    var scale = 1, tx = 0, ty = 0;
    function apply() { svg.style.transform = "translate(" + tx + "px," + ty + "px) scale(" + scale + ")"; }
    function zoomAt(cx, cy, factor) {
      var next = Math.min(8, Math.max(0.1, scale * factor));
      tx = cx - (cx - tx) * (next / scale);
      ty = cy - (cy - ty) * (next / scale);
      scale = next;
      apply();
    }
    function fit() {
      var stageRect = stage.getBoundingClientRect();
      var size = svgSize();
      if (!size.w || !size.h) { return; }
      var pad = 24;
      var svgRect = svg.getBoundingClientRect();
      var natW = svgRect.width / scale, natH = svgRect.height / scale;
      if (!natW || !natH) { natW = size.w; natH = size.h; }
      scale = Math.min((stageRect.width - pad) / natW, (stageRect.height - pad) / natH, 4);
      tx = (stageRect.width - natW * scale) / 2;
      ty = (stageRect.height - natH * scale) / 2;
      apply();
    }

    // "Find node…" jump target: reuses the existing anchor-preserving zoom, then a pure
    // screen-space pan (via getBoundingClientRect, so it's correct regardless of the
    // SVG's internal coordinate system) to bring the node to the stage's centre.
    function centerOn(node) {
      var stageRect = stage.getBoundingClientRect();
      var r = node.getBoundingClientRect();
      if (scale < 1.5) {
        zoomAt(r.left + r.width / 2 - stageRect.left, r.top + r.height / 2 - stageRect.top, 1.5 / scale);
        r = node.getBoundingClientRect();
      }
      tx += (stageRect.left + stageRect.width / 2) - (r.left + r.width / 2);
      ty += (stageRect.top + stageRect.height / 2) - (r.top + r.height / 2);
      apply();
    }

    card.querySelector("[data-act='zoom-in']").onclick = function () {
      var r = stage.getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 1.2);
    };
    card.querySelector("[data-act='zoom-out']").onclick = function () {
      var r = stage.getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 1 / 1.2);
    };
    card.querySelector("[data-act='zoom-reset']").onclick = function () { scale = 1; tx = 0; ty = 0; apply(); };
    var fitBtn = card.querySelector("[data-act='fit']");
    if (fitBtn) { fitBtn.onclick = fit; }
    card.querySelector("[data-act='png']").onclick = downloadPng;
    var svgBtn = card.querySelector("[data-act='svg']");
    if (svgBtn) { svgBtn.onclick = downloadSvg; }
    var copyBtn = card.querySelector("[data-act='copy']");
    if (copyBtn) {
      copyBtn.onclick = function () {
        var src = card.querySelector(".mermaid-src");
        if (!src) { return; }
        var done = function () {
          var old = copyBtn.textContent;
          copyBtn.textContent = "✓ Copied";
          setTimeout(function () { copyBtn.textContent = old; }, 1500);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(src.textContent).then(done).catch(function () { fallbackCopy(src.textContent); done(); });
        } else { fallbackCopy(src.textContent); done(); }
      };
    }

    on(stage, "wheel", function (e) {
      e.preventDefault();
      var r = stage.getBoundingClientRect();
      zoomAt(e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.1 : 1 / 1.1);
    }, { passive: false });

    var dragging = false, lastX = 0, lastY = 0;
    on(stage, "mousedown", function (e) {
      dragging = true; lastX = e.clientX; lastY = e.clientY; stage.classList.add("panning");
    });
    on(stage, "dblclick", function () { scale = 1; tx = 0; ty = 0; apply(); });
    on(window, "mousemove", function (e) {
      if (!dragging) { return; }
      tx += e.clientX - lastX; ty += e.clientY - lastY;
      lastX = e.clientX; lastY = e.clientY;
      apply();
    });
    on(window, "mouseup", function () { dragging = false; stage.classList.remove("panning"); });

    attachTooltips(card, svg, centerOn);

    // Auto-fit once the SVG has laid out so the whole diagram is visible on load
    // without clicking Fit. Double rAF lets mermaid's SVG size settle before measuring.
    // Only ever runs for a rendered (visible) card, so the stage has real dimensions.
    requestAnimationFrame(function () { requestAnimationFrame(fit); });

    function fallbackCopy(text) {
      var ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); } catch (e) { }
      document.body.removeChild(ta);
    }

    function serializeSvg() {
      var clone = svg.cloneNode(true);
      clone.style.transform = "";
      clone.removeAttribute("style");
      if (!clone.getAttribute("xmlns")) { clone.setAttribute("xmlns", "http://www.w3.org/2000/svg"); }
      return new XMLSerializer().serializeToString(clone);
    }

    function svgSize() {
      var vb = (svg.getAttribute("viewBox") || "").split(/[\s,]+/).map(Number);
      if (vb.length === 4 && vb[2] > 0 && vb[3] > 0) { return { w: vb[2], h: vb[3] }; }
      var box = svg.getBBox();
      return { w: box.width, h: box.height };
    }

    function downloadSvg() {
      var blob = new Blob([serializeSvg()], { type: "image/svg+xml;charset=utf-8" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = (card.dataset.pngName || "archdiagram") + ".svg";
      a.click();
      URL.revokeObjectURL(a.href);
    }

    function downloadPng() {
      var size = svgSize();
      // 2x for crisp raster, clamped so huge diagrams stay under canvas limits.
      var s = Math.min(2, 8192 / Math.max(size.w, size.h));
      var canvas = document.createElement("canvas");
      canvas.width = Math.ceil(size.w * s);
      canvas.height = Math.ceil(size.h * s);
      var ctx = canvas.getContext("2d");
      ctx.fillStyle = getComputedStyle(stage).backgroundColor || "#ffffff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      var url = URL.createObjectURL(new Blob([serializeSvg()], { type: "image/svg+xml;charset=utf-8" }));
      var img = new Image();
      img.onload = function () {
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        URL.revokeObjectURL(url);
        canvas.toBlob(function (blob) {
          var a = document.createElement("a");
          a.href = URL.createObjectURL(blob);
          a.download = (card.dataset.pngName || "archdiagram") + ".png";
          a.click();
          URL.revokeObjectURL(a.href);
        }, "image/png");
      };
      img.onerror = function () { URL.revokeObjectURL(url); };
      img.src = url;
    }
  }

  function attachTooltips(card, svg, centerOn) {
    var mapEl = card.querySelector("script.tooltips");
    var map = {};
    if (mapEl) { try { map = JSON.parse(mapEl.textContent); } catch (e) { map = {}; } }
    var hrefEl = card.querySelector("script.hrefs");
    var hrefs = {};
    if (hrefEl) { try { hrefs = JSON.parse(hrefEl.textContent); } catch (e) { hrefs = {}; } }
    var adjEl = card.querySelector("script.adjacency");
    var adjacency = {};
    if (adjEl) { try { adjacency = JSON.parse(adjEl.textContent); } catch (e) { adjacency = {}; } }

    var nodeByAlias = {};
    svg.querySelectorAll("g.node").forEach(function (node) {
      // Mermaid node DOM ids embed our alias, e.g. "flowchart-n001-12".
      // Aliases are zero-padded to >=3 digits but grow past n999 on large diagrams,
      // so match 3-or-more digits (n\d{3,}) — not exactly 3 — or links break at scale.
      var m = /(?:^|-)(n\d{3,})(?:-|$)/.exec(node.id || "");
      var alias = m && m[1];
      if (!alias) { return; }
      nodeByAlias[alias] = node;
      var text = map[alias];
      var url = hrefs[alias];

      bindTip(node, text);

      if (url) {
        node.classList.add("clickable-node");
        node.addEventListener("click", function () { window.location.href = url; });
      } else if (text) {
        node.style.cursor = "pointer";
      }
    });

    setupFlowHighlight(card, svg, nodeByAlias, adjacency);
    setupNodeFind(card, nodeByAlias, centerOn);
  }

  /* ---- Hover flow-path highlight: light a node's neighbours, dim the rest ----
     Edge dimming is best-effort: mermaid's rendered edge element ids are version-
     specific ("L_n001_n002_0" in the vendored build here — verified against the
     source below). If that pattern ever matches nothing (a mermaid upgrade changed
     it), highlighting silently degrades to nodes-only, which still delivers the
     core value and never breaks node click-to-open. */
  function setupFlowHighlight(card, svg, nodeByAlias, adjacency) {
    var aliases = Object.keys(nodeByAlias);
    if (aliases.length === 0) { return; }
    var edgeEls = svg.querySelectorAll(".edgePaths path, .edgePath path, path.flowchart-link");

    function edgeTouches(el, alias) {
      var id = el.id || el.getAttribute("class") || "";
      return id.indexOf("_" + alias + "_") >= 0 || id.indexOf("-" + alias + "-") >= 0
          || id.indexOf("_" + alias) === id.length - alias.length - 1
          || id.indexOf(alias + "_") === 0 || id.indexOf(alias + "-") === 0;
    }

    function highlight(alias) {
      var keep = {}; keep[alias] = true;
      (adjacency[alias] || []).forEach(function (n) { keep[n] = true; });
      svg.classList.add("path-focus");
      aliases.forEach(function (a) {
        nodeByAlias[a].classList.toggle("path-dim", !keep[a]);
      });
      edgeEls.forEach(function (el) { el.classList.toggle("path-dim", !edgeTouches(el, alias)); });
    }
    function clear() {
      svg.classList.remove("path-focus");
      svg.querySelectorAll(".path-dim").forEach(function (el) { el.classList.remove("path-dim"); });
    }

    aliases.forEach(function (a) {
      var node = nodeByAlias[a];
      node.addEventListener("mouseenter", function () { highlight(a); });
      node.addEventListener("mouseleave", clear);
      node.addEventListener("focus", function () { highlight(a); });
      node.addEventListener("blur", clear);
    });
  }

  /* ---- "Find node…" toolbar box: centers + pulses the matching node ---- */
  function setupNodeFind(card, nodeByAlias, centerOn) {
    var input = card.querySelector("[data-act='find']");
    if (!input) { return; }

    function labelOf(node) {
      var t = node.querySelector("text, .nodeLabel, span");
      return (t ? t.textContent : node.textContent || "").trim();
    }

    function findAndGo() {
      var q = input.value.trim().toLowerCase();
      if (!q) { return; }
      var aliases = Object.keys(nodeByAlias);
      for (var i = 0; i < aliases.length; i++) {
        var node = nodeByAlias[aliases[i]];
        if (labelOf(node).toLowerCase().indexOf(q) >= 0) {
          centerOn(node);
          node.classList.add("path-pulse");
          setTimeout(function () { node.classList.remove("path-pulse"); }, 1200);
          return;
        }
      }
    }
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); findAndGo(); } });
  }

  // Selector groups: <select data-diagram-select="group"> shows one card per group.
  document.querySelectorAll("select[data-diagram-select]").forEach(function (sel) {
    var group = sel.getAttribute("data-diagram-select");
    function update() {
      document.querySelectorAll(".diagram-card[data-group='" + group + "']").forEach(function (card) {
        var show = card.id === sel.value;
        card.hidden = !show;
        if (show) { renderCard(card); }
      });
    }
    sel.addEventListener("change", update);
    update();
  });

  // Metrics scatter: give every dot the same rich hover card as diagram nodes,
  // plus keyboard focus (the dots carry tabindex + data-tip from the server).
  document.querySelectorAll(".metrics-scatter [data-tip]").forEach(function (el) {
    bindTip(el, el.getAttribute("data-tip"));
  });

  // Metrics: offline zone calculator (no network). Mirrors ArchitectureMetrics.Classify.
  (function () {
    var box = document.getElementById("zone-calc");
    if (!box) { return; }
    var ca = box.querySelector("#calc-ca"), ce = box.querySelector("#calc-ce");
    var abs = box.querySelector("#calc-abs"), total = box.querySelector("#calc-total");
    var outI = box.querySelector("#calc-i"), outA = box.querySelector("#calc-a"), outD = box.querySelector("#calc-d");
    var verdict = box.querySelector("#calc-verdict");
    function num(el) { var v = parseInt(el.value, 10); return isNaN(v) || v < 0 ? 0 : v; }
    function cls(el, k) { el.className = "badge" + (k ? " " + k : ""); }
    function zoneVerdict(I, A, D, Ca, Ce, Ab, Tot) {
      if ((Ca + Ce) === 0) {
        return "Isolated module — no dependencies in or out. Instability is undefined; treated as 0.";
      }
      if (I <= 0.3 && A <= 0.3 && Ca > 0) {
        return "<strong>Zone of pain</strong> — rigid and heavily depended-on. Add abstractions "
             + "(interfaces/abstract base types) so dependents rely on contracts, or raise Ce toward "
             + Ca + " to increase instability.";
      }
      if (I >= 0.7 && A >= 0.7) {
        return "<strong>Zone of uselessness</strong> — abstract but barely used. Delete unused "
             + "abstractions or give this module concrete work.";
      }
      if (D <= 0.3) { return "<strong>Healthy</strong> — close to the main sequence."; }
      if (A <= 0.3 && I <= 0.3 && Ca === 0) {
        return "Concrete leaf with no dependents — the high distance is a formula artifact, not a real problem.";
      }
      return "<strong>Watch</strong> — D=" + D.toFixed(2) + " is off the main sequence; "
           + "nudge abstractness or coupling toward A + I = 1.";
    }
    function recompute() {
      var Ca = num(ca), Ce = num(ce), Ab = num(abs), Tot = num(total);
      var I = (Ca + Ce) === 0 ? 0 : Ce / (Ca + Ce);
      var A = Tot === 0 ? 0 : Math.min(1, Ab / Tot);
      var D = Math.abs(A + I - 1);
      outI.textContent = "I " + I.toFixed(2);
      outA.textContent = "A " + A.toFixed(2);
      outD.textContent = "D " + D.toFixed(2);
      cls(outD, D <= 0.3 ? "ok" : D <= 0.6 ? "" : "warn");
      verdict.innerHTML = zoneVerdict(I, A, D, Ca, Ce, Ab, Tot);
    }
    ["input", "change"].forEach(function (ev) {
      [ca, ce, abs, total].forEach(function (el) { el.addEventListener(ev, recompute); });
    });
    recompute();
  })();

  // Render all initially-visible cards. Cards marked data-deferred are rendered
  // by a page-specific controller (e.g. the landscape layer filters) instead.
  document.querySelectorAll(".diagram-card:not([hidden]):not([data-deferred])").forEach(renderCard);

  // Public hook so page-specific controllers can swap a card's Mermaid source and
  // force a re-render through the same path (used by the landscape layer filters).
  window.ArchViewer = {
    rerenderCard: function (card) {
      if (!card) { return; }
      if (card._ac) { card._ac.abort(); }
      delete card.dataset.rendered;
      var target = card.querySelector(".render-target");
      if (target) { target.innerHTML = ""; }
      renderCard(card);
    }
  };

  // Theme toggle: swap theme, re-init mermaid, and re-render every already-rendered card.
  var toggle = document.getElementById("theme-toggle");
  if (toggle) {
    toggle.onclick = function () {
      var cur = currentTheme() === "dark" ? "light" : "dark";
      document.documentElement.setAttribute("data-theme", cur);
      try { localStorage.setItem("archdiagram-theme", cur); } catch (e) { }
      initMermaid();
      document.querySelectorAll(".diagram-card[data-rendered]").forEach(function (card) {
        delete card.dataset.rendered;
        if (!card.hidden) { renderCard(card); }
      });
    };
  }

  /* ---- Test-file visibility toggle ----
     Tests are hidden by default (root class .hide-tests applied pre-paint in the template).
     This button flips it, persists the choice, prunes now-empty structure-tree folders, and
     keeps its own label in sync. Presentation only — nothing is removed from the model/search. */
  (function () {
    var btn = document.getElementById("tests-toggle");
    if (!btn) { return; }
    var root = document.documentElement;
    var tree = document.getElementById("structure-tree");

    function pruneTests() {
      if (!tree) { return; }
      var hiding = root.classList.contains("hide-tests");
      // Deepest-first so a parent sees its children's already-computed hidden state.
      var all = Array.prototype.slice.call(tree.querySelectorAll("details")).reverse();
      all.forEach(function (d) {
        if (!hiding) { d.hidden = false; return; }
        var visibleFile = d.querySelector(":scope > ul > li[data-path]:not([data-test])");
        var visibleChild = d.querySelector(":scope > details:not([hidden])");
        d.hidden = !visibleFile && !visibleChild;
      });
    }

    function pruneSections() {
      // Hide a Types-page namespace section when all its type cards are test files.
      var hiding = root.classList.contains("hide-tests");
      document.querySelectorAll("section.ns-group").forEach(function (sec) {
        if (!hiding) { sec.hidden = false; return; }
        sec.hidden = !sec.querySelector(".type-card:not([data-test])");
      });
    }

    function sync() {
      var hidden = root.classList.contains("hide-tests");
      btn.textContent = "🧪 Tests: " + (hidden ? "hidden" : "shown");
      pruneTests();
      pruneSections();
    }

    // Keep the 3D graph's own "Hide test files" checkbox in step with the global toggle
    // (the graph is WebGL, not CSS, so the .hide-tests class can't reach it).
    function syncGraph(hidden) {
      var g3d = document.getElementById("g3d-hide-tests");
      if (g3d && g3d.checked !== hidden) {
        g3d.checked = hidden;
        g3d.dispatchEvent(new Event("change"));
      }
    }

    btn.onclick = function () {
      var hidden = root.classList.toggle("hide-tests");
      try { localStorage.setItem("archdiagram-show-tests", hidden ? "0" : "1"); } catch (e) { }
      sync();
      syncGraph(hidden);
    };
    sync();
  })();

  /* ---- Ctrl+K search palette ---- */
  (function () {
    var overlay = document.getElementById("palette");
    var input = document.getElementById("palette-input");
    var list = document.getElementById("palette-results");
    var openBtn = document.getElementById("search-open");
    if (!overlay || !input || !list) { return; }
    var index = window.ARCH_SEARCH_INDEX || [];
    var relRoot = overlay.getAttribute("data-rel-root") || "";
    var selected = 0, hits = [];

    function open() {
      overlay.hidden = false;
      input.value = "";
      search("");
      input.focus();
    }
    function close() { overlay.hidden = true; }

    function score(name, detail, q) {
      var n = name.toLowerCase(), d = (detail || "").toLowerCase();
      var i = n.indexOf(q);
      if (i === 0) { return 100; }
      if (i > 0) { return n.length - i > 0 ? 60 - Math.min(40, i) : 0; }
      if (d.indexOf(q) >= 0) { return 10; }
      // All query chars in order (subsequence match).
      var pos = -1;
      for (var c = 0; c < q.length; c++) {
        pos = n.indexOf(q[c], pos + 1);
        if (pos < 0) { return 0; }
      }
      return 5;
    }

    function search(q) {
      q = q.trim().toLowerCase();
      hits = [];
      if (q.length === 0) {
        for (var i = 0; i < index.length && hits.length < 12; i++) {
          if (index[i][0] === "file") { hits.push(index[i]); }
        }
      } else {
        var scored = [];
        for (var j = 0; j < index.length; j++) {
          var s = score(index[j][1], index[j][2], q);
          if (s > 0) { scored.push([s, index[j]]); }
        }
        scored.sort(function (a, b) { return b[0] - a[0]; });
        hits = scored.slice(0, 20).map(function (x) { return x[1]; });
      }
      selected = 0;
      renderList();
    }

    function renderList() {
      list.innerHTML = "";
      if (hits.length === 0) {
        var li = document.createElement("li");
        li.className = "palette-empty";
        li.textContent = "No matches";
        list.appendChild(li);
        return;
      }
      hits.forEach(function (h, i) {
        var li = document.createElement("li");
        if (i === selected) { li.className = "selected"; }
        var kind = document.createElement("span");
        kind.className = "palette-kind";
        kind.textContent = h[0];
        var name = document.createElement("span");
        name.className = "palette-name";
        name.textContent = h[1];
        var detail = document.createElement("span");
        detail.className = "palette-detail";
        detail.textContent = h[2] || "";
        li.appendChild(kind); li.appendChild(name); li.appendChild(detail);
        li.addEventListener("click", function () { go(h); });
        li.addEventListener("mousemove", function () {
          if (selected !== i) { selected = i; renderList(); }
        });
        list.appendChild(li);
      });
      var sel = list.querySelector(".selected");
      if (sel) { sel.scrollIntoView({ block: "nearest" }); }
    }

    function go(h) { window.location.href = relRoot + h[3]; }

    input.addEventListener("input", function () { search(input.value); });
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); selected = Math.min(hits.length - 1, selected + 1); renderList(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); selected = Math.max(0, selected - 1); renderList(); }
      else if (e.key === "Enter" && hits[selected]) { go(hits[selected]); }
      else if (e.key === "Escape") { close(); }
    });
    overlay.addEventListener("mousedown", function (e) { if (e.target === overlay) { close(); } });
    if (openBtn) { openBtn.onclick = open; }
    window.addEventListener("keydown", function (e) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); overlay.hidden ? open() : close(); }
      else if (e.key === "/" && overlay.hidden && !/^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName)) { e.preventDefault(); open(); }
      else if (e.key === "Escape" && !overlay.hidden) { close(); }
    });
  })();

  /* ---- Generic card filter: <input class="filter-input" data-filter-target="sel"> ---- */
  document.querySelectorAll(".filter-input[data-filter-target]").forEach(function (input) {
    var groupSel = input.getAttribute("data-filter-target");
    var countEl = input.parentElement.querySelector(".filter-count");
    input.addEventListener("input", function () {
      var q = input.value.trim().toLowerCase();
      var visible = 0, total = 0;
      document.querySelectorAll(groupSel).forEach(function (group) {
        var any = false;
        group.querySelectorAll(".filterable").forEach(function (card) {
          total++;
          var show = q.length === 0 || (card.dataset.search || "").indexOf(q) >= 0;
          card.hidden = !show;
          if (show) { any = true; visible++; }
        });
        group.hidden = !any;
      });
      if (countEl) { countEl.textContent = q.length === 0 ? "" : visible + " of " + total + " shown"; }
    });
  });

  /* ---- Structure tree: filter + expand/collapse ---- */
  (function () {
    var tree = document.getElementById("structure-tree");
    if (!tree) { return; }
    var filter = document.getElementById("tree-filter");
    var expand = document.getElementById("tree-expand");
    var collapse = document.getElementById("tree-collapse");
    var countEl = document.querySelector(".select-row .filter-count");

    if (expand) { expand.onclick = function () { tree.querySelectorAll("details").forEach(function (d) { d.open = true; }); }; }
    if (collapse) { collapse.onclick = function () { tree.querySelectorAll("details").forEach(function (d) { d.open = false; }); }; }

    if (filter) {
      filter.addEventListener("input", function () {
        var q = filter.value.trim().toLowerCase();
        var visible = 0, total = 0;
        tree.querySelectorAll("li[data-path]").forEach(function (li) {
          total++;
          var show = q.length === 0 || li.dataset.path.indexOf(q) >= 0;
          li.hidden = !show;
          if (show) { visible++; }
        });
        // Hide folders with no visible files; open matches while filtering.
        function prune(details) {
          var any = false;
          details.querySelectorAll(":scope > details").forEach(function (child) {
            if (prune(child)) { any = true; }
          });
          details.querySelectorAll(":scope > ul > li[data-path]").forEach(function (li) {
            if (!li.hidden) { any = true; }
          });
          details.hidden = q.length > 0 && !any;
          if (q.length > 0 && any) { details.open = true; }
          return any;
        }
        tree.querySelectorAll(":scope > details").forEach(prune);
        if (countEl) { countEl.textContent = q.length === 0 ? "" : visible + " of " + total + " files"; }
      });
    }
  })();

  /* ---- Landscape layer filters ----
     Rebuilds the Mermaid source from the original embedded diagram so the
     service-call and shared-package layers toggle independently, low-volume call
     edges threshold out, and call edges weight by volume. Self-guards: on any page
     without #landscape-filters it returns immediately. */
  (function () {
    var card = document.getElementById("landscape");
    var bar = document.getElementById("landscape-filters");
    var src = card && card.querySelector(".mermaid-src");
    if (!card || !bar || !src || !window.ArchViewer) { return; }

    var header = [], siteNodes = [], pkgNodes = [], calls = [], pkgLinks = [], pkgEdges = [];
    src.textContent.split(/\r?\n/).forEach(function (raw) {
      var line = raw.trim();
      if (!line) { return; }
      if (/^flowchart/.test(line) || /^classDef/.test(line)) { header.push(line); return; }
      var call = /^(\S+)\s*-\.->\|"(\d+)\s*calls?"\|\s*(\S+)/.exec(line);
      if (call) { calls.push({ from: call[1], to: call[3], count: +call[2] }); return; }
      if (/^\S+\s*-->\|/.test(line)) { pkgLinks.push(line); return; }
      if (/^\S+\s*-\.->\s*\S+\s*$/.test(line)) { pkgEdges.push(line); return; }
      var node = /^(n\d{3,})\s*[\[{]/.exec(line);
      if (node) { (/:::external/.test(line) ? pkgNodes : siteNodes).push(line); }
    });

    var maxCount = calls.reduce(function (m, c) { return Math.max(m, c.count); }, 1);
    var thresholdEl = document.getElementById("lf-threshold");
    thresholdEl.max = Math.ceil(maxCount / 25) * 25;

    var cbCalls = document.getElementById("lf-calls");
    var cbPkgs = document.getElementById("lf-packages");
    var cbLinks = document.getElementById("lf-pkglinks");
    var valEl = document.getElementById("lf-threshold-val");
    var summaryEl = document.getElementById("lf-summary");

    function rebuild() {
      var thr = +thresholdEl.value;
      valEl.textContent = thr;
      var lines = header.slice().concat(siteNodes);
      if (cbPkgs.checked) { lines = lines.concat(pkgNodes); }
      var edgeIdx = -1, styles = [];
      if (cbLinks.checked) { pkgLinks.forEach(function (l) { lines.push(l); edgeIdx++; }); }
      if (cbPkgs.checked) { pkgEdges.forEach(function (l) { lines.push(l); edgeIdx++; }); }
      var shownCalls = 0;
      if (cbCalls.checked) {
        calls.forEach(function (c) {
          if (c.count < thr) { return; }
          lines.push(c.from + ' -.->|"' + c.count + ' calls"| ' + c.to);
          edgeIdx++; shownCalls++;
          var w = (1.5 + 4.5 * (c.count / maxCount)).toFixed(1);
          styles.push("linkStyle " + edgeIdx + " stroke-width:" + w + "px;");
        });
      }
      lines = lines.concat(styles);
      src.textContent = lines.join("\n");
      window.ArchViewer.rerenderCard(card);
      summaryEl.textContent =
        (cbCalls.checked ? shownCalls + " calls (≥" + thr + ")" : "calls hidden") +
        " · " + (cbLinks.checked ? pkgLinks.length + " package links" : "links hidden") +
        " · " + (cbPkgs.checked ? pkgNodes.length + " shared packages" : "packages hidden");
    }

    [cbCalls, cbPkgs, cbLinks].forEach(function (el) { el.addEventListener("change", rebuild); });
    thresholdEl.addEventListener("input", rebuild);
    bar.hidden = false;
    rebuild();
  })();

  /* ---- Dependencies page: internal/external layer toggle + highlight filter ----
     Parses the visible dep card's embedded Mermaid source and rebuilds it from the
     control state, then re-renders. External nodes carry ":::external"; edges to them
     are dashed ("-.->"). Highlight dims (opacity) non-matching nodes/edges via appended
     style/linkStyle statements. State is persisted (E1); quick-filter chips are built
     from the visible card's external packages (E2). Self-guards on #dep-filters. */
  (function () {
    var bar = document.getElementById("dep-filters");
    if (!bar || !window.ArchViewer) { return; }
    var cbInternal = document.getElementById("dep-internal");
    var cbExternal = document.getElementById("dep-external");
    var filterEl = document.getElementById("dep-filter");
    var chipsEl = document.getElementById("dep-chips");
    var summaryEl = document.getElementById("dep-summary");
    var sel = document.querySelector("select[data-diagram-select='deps']");

    // E1: persist toggle state (filter text stays transient). Guarded for file://.
    var STORE_KEY = "archdiagram-deps-filter";
    function loadPrefs() { try { return JSON.parse(localStorage.getItem(STORE_KEY) || "{}") || {}; } catch (e) { return {}; } }
    function savePrefs() {
      try { localStorage.setItem(STORE_KEY, JSON.stringify({ internal: cbInternal.checked, external: cbExternal.checked })); } catch (e) { }
    }
    var p0 = loadPrefs();
    if (typeof p0.internal === "boolean") { cbInternal.checked = p0.internal; }
    if (typeof p0.external === "boolean") { cbExternal.checked = p0.external; }

    function visibleCard() {
      var cards = document.querySelectorAll(".diagram-card[data-group='deps']");
      for (var i = 0; i < cards.length; i++) { if (!cards[i].hidden) { return cards[i]; } }
      return null;
    }

    // Classify one Mermaid line. Alias regex is n\d{3,} (grows past n999 on big diagrams).
    function parse(text) {
      var header = [], intNodes = [], extNodes = [], edges = [];
      text.split(/\r?\n/).forEach(function (raw) {
        var line = raw.trim();
        if (!line) { return; }
        if (/^flowchart/.test(line) || /^classDef/.test(line)) { header.push(line); return; }
        var edge = /^(n\d{3,})\s*-(\.?)->(?:\|"[^"]*"\|)?\s*(n\d{3,})/.exec(line);
        if (edge) { edges.push({ line: line, from: edge[1], to: edge[3] }); return; }
        var node = /^(n\d{3,})\s*[\[{(]/.exec(line);
        if (node) {
          var isExt = /:::external/.test(line);
          (isExt ? extNodes : intNodes).push({ alias: node[1], line: line });
        }
      });
      return { header: header, intNodes: intNodes, extNodes: extNodes, edges: edges };
    }

    function labelOf(nodeLine) {
      var m = /["']([^"']*)["']/.exec(nodeLine); // first quoted label
      return (m ? m[1] : "");
    }

    // E2: chips for the visible card's external packages (already count-desc ordered).
    function renderChips(extNodes) {
      if (!chipsEl) { return; }
      chipsEl.innerHTML = "";
      extNodes.slice(0, 8).forEach(function (n) {
        var name = labelOf(n.line);
        if (!name) { return; }
        var b = document.createElement("button");
        b.type = "button";
        b.className = "btn";
        b.style.padding = ".15rem .5rem";
        b.style.fontSize = ".75rem";
        b.textContent = name;
        b.addEventListener("click", function () { filterEl.value = name; apply(); });
        chipsEl.appendChild(b);
      });
    }

    function rebuild(card) {
      if (!card) { return; }
      if (card.dataset.depOriginal == null) {
        var src0 = card.querySelector(".mermaid-src");
        if (!src0) { return; }
        card.dataset.depOriginal = src0.textContent;
      }
      var p = parse(card.dataset.depOriginal);
      renderChips(p.extNodes);
      var showInt = cbInternal.checked, showExt = cbExternal.checked;
      var q = (filterEl.value || "").trim().toLowerCase();

      var live = {};
      p.intNodes.forEach(function (n) { if (showInt) { live[n.alias] = n; } });
      p.extNodes.forEach(function (n) { if (showExt) { live[n.alias] = n; } });

      function matches(alias) {
        if (!q) { return true; }
        var n = live[alias];
        return !!n && labelOf(n.line).toLowerCase().indexOf(q) >= 0;
      }

      var lines = p.header.slice();
      Object.keys(live).forEach(function (a) { lines.push(live[a].line); });
      var kept = [];
      p.edges.forEach(function (e) {
        if (!live[e.from] || !live[e.to]) { return; }
        kept.push(e);
        lines.push(e.line);
      });

      var shown = 0;
      if (q) {
        Object.keys(live).forEach(function (a) {
          if (matches(a)) { shown++; } else { lines.push("style " + a + " opacity:0.15"); }
        });
        kept.forEach(function (e, i) {
          if (!(matches(e.from) || matches(e.to))) { lines.push("linkStyle " + i + " opacity:0.12"); }
        });
      } else {
        shown = Object.keys(live).length;
      }

      card.querySelector(".mermaid-src").textContent = lines.join("\n");
      window.ArchViewer.rerenderCard(card);
      summaryEl.textContent =
        (showInt ? "internal on" : "internal off") + " · " +
        (showExt ? "external on" : "external off") +
        (q ? " · " + shown + " match “" + q + "”" : "");
    }

    function active() { return !cbInternal.checked || !cbExternal.checked || filterEl.value.trim().length > 0; }
    function apply() { rebuild(visibleCard()); }
    [cbInternal, cbExternal].forEach(function (el) {
      el.addEventListener("change", function () { savePrefs(); apply(); });
    });
    filterEl.addEventListener("input", apply);

    // Rebuild chips (and re-apply if filters are active) for whatever card is now shown.
    function refresh() {
      var card = visibleCard();
      if (!card) { return; }
      if (active()) { rebuild(card); return; }
      var src = card.querySelector(".mermaid-src");
      renderChips(src ? parse(card.dataset.depOriginal != null ? card.dataset.depOriginal : src.textContent).extNodes : []);
    }
    // site.js's own change handler renders the pristine new card first; refresh after it.
    if (sel) { sel.addEventListener("change", function () { setTimeout(refresh, 0); }); }
    bar.hidden = false;
    refresh();
  })();
})();

// ---- Explain (ⓘ) popovers: Simple + Go deeper, from the embedded glossary ----
(function () {
  var pop = document.getElementById("explain-pop");
  var dataEl = document.getElementById("arch-glossary");
  if (!pop || !dataEl) { return; }
  var glossary = {};
  try { glossary = JSON.parse(dataEl.textContent) || {}; } catch (e) { glossary = {}; }

  function title(term) {
    return term.replace(/-/g, " ").replace(/\b\w/g, function (c) { return c.toUpperCase(); });
  }
  function esc(s) { return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }

  var current = null;
  function close() { pop.hidden = true; current = null; }

  function open(btn) {
    var term = btn.getAttribute("data-term");
    var entry = glossary[term];
    if (!entry) { return; }
    current = btn;
    pop.innerHTML =
      '<div class="exp-term">' + esc(title(term)) + '</div>' +
      '<div class="exp-simple">' + esc(entry.simple) + '</div>' +
      (entry.detail ? '<button class="exp-more" type="button">Go deeper ▾</button>' +
        '<div class="exp-detail" hidden>' + esc(entry.detail) + '</div>' : '');
    pop.hidden = false;
    var more = pop.querySelector(".exp-more");
    if (more) {
      more.addEventListener("click", function () {
        var d = pop.querySelector(".exp-detail");
        var show = d.hidden;
        d.hidden = !show;
        more.textContent = show ? "Show less ▴" : "Go deeper ▾";
      });
    }
    // Position below the button, clamped to the viewport.
    var r = btn.getBoundingClientRect();
    var x = Math.min(r.left, window.innerWidth - pop.offsetWidth - 10);
    var y = r.bottom + 6;
    if (y + pop.offsetHeight > window.innerHeight - 8) { y = Math.max(8, r.top - pop.offsetHeight - 6); }
    pop.style.left = Math.max(8, x) + "px";
    pop.style.top = y + "px";
  }

  document.addEventListener("click", function (e) {
    var btn = e.target.closest && e.target.closest(".explain");
    if (btn) { e.preventDefault(); if (current === btn) { close(); } else { open(btn); } return; }
    if (!pop.hidden && !pop.contains(e.target)) { close(); }
  });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") { close(); } });
})();

// ---- Mobile: off-canvas sidebar toggle ----
(function () {
  var toggle = document.getElementById("nav-toggle");
  var layout = document.querySelector(".layout");
  var overlay = document.getElementById("nav-overlay");
  if (!toggle || !layout) { return; }
  function open() { layout.classList.add("nav-open"); toggle.setAttribute("aria-expanded", "true"); if (overlay) { overlay.hidden = false; } }
  function close() { layout.classList.remove("nav-open"); toggle.setAttribute("aria-expanded", "false"); }
  toggle.addEventListener("click", function () { layout.classList.contains("nav-open") ? close() : open(); });
  if (overlay) { overlay.addEventListener("click", close); }
  document.querySelectorAll(".sidebar nav a").forEach(function (a) { a.addEventListener("click", close); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") { close(); } });
})();
