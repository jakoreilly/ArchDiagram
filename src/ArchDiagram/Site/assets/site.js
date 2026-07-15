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

    attachTooltips(card, svg);

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

  function attachTooltips(card, svg) {
    var mapEl = card.querySelector("script.tooltips");
    var map = {};
    if (mapEl) { try { map = JSON.parse(mapEl.textContent); } catch (e) { map = {}; } }
    var hrefEl = card.querySelector("script.hrefs");
    var hrefs = {};
    if (hrefEl) { try { hrefs = JSON.parse(hrefEl.textContent); } catch (e) { hrefs = {}; } }

    svg.querySelectorAll("g.node").forEach(function (node) {
      // Mermaid node DOM ids embed our alias, e.g. "flowchart-n001-12".
      // Aliases are zero-padded to >=3 digits but grow past n999 on large diagrams,
      // so match 3-or-more digits (n\d{3,}) — not exactly 3 — or links break at scale.
      var m = /(?:^|-)(n\d{3,})(?:-|$)/.exec(node.id || "");
      var alias = m && m[1];
      if (!alias) { return; }
      var text = map[alias];
      var url = hrefs[alias];

      if (text && tipEl) {
        node.addEventListener("mousemove", function (e) {
          tipEl.textContent = text;
          tipEl.hidden = false;
          var x = Math.min(e.clientX + 14, window.innerWidth - tipEl.offsetWidth - 8);
          var y = Math.min(e.clientY + 14, window.innerHeight - tipEl.offsetHeight - 8);
          tipEl.style.left = x + "px";
          tipEl.style.top = y + "px";
        });
        node.addEventListener("mouseleave", function () { tipEl.hidden = true; });
      }

      if (url) {
        node.classList.add("clickable-node");
        node.addEventListener("click", function () { window.location.href = url; });
      } else if (text) {
        node.style.cursor = "pointer";
      }
    });
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
