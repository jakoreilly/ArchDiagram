/* ArchDiagram layered 3D view — a "chess board" of architectural tiers.
   Modules are pinned to an elevation by their stability tier (top = orchestration,
   base = foundational); dependency edges run between the planes, red when they point
   "up" (against the grain). Reads window.ARCH_LAYERS, reuses the vendored ForceGraph3D
   bundle, fully offline. Degrades to a message when WebGL/the bundle is unavailable. */
(function () {
  "use strict";
  var root = document.getElementById("layered3d-root");
  var canvas = document.getElementById("layered3d-canvas");
  var data = window.ARCH_LAYERS;
  if (!root || !canvas || !data || !data.nodes || !data.nodes.length) { return; }

  function fail(msg) {
    root.innerHTML = '<div class="panel empty-state"><div class="big">▦</div><p>' + msg +
      ' The tier list above shows the same layering in 2D.</p></div>';
  }
  if (typeof ForceGraph3D !== "function") { fail("The 3D engine could not be loaded."); return; }
  try {
    var probe = document.createElement("canvas");
    if (!(probe.getContext("webgl") || probe.getContext("experimental-webgl"))) { fail("This view needs WebGL."); return; }
  } catch (e) { fail("This view needs WebGL."); return; }

  function cssVar(n) { return getComputedStyle(document.documentElement).getPropertyValue(n).trim(); }
  var DANGER = cssVar("--danger") || "#c0392b";
  var MUTED = cssVar("--text-soft") || "#888";

  // Tier palette top → bottom (orchestration is "hot", foundation is "cool/stable").
  var PALETTE = ["#c0392b", "#b7791f", "#2f6fab", "#2e7d32", "#6b46c1", "#1f8a8a"];
  var tierCount = (data.tiers && data.tiers.length) || 1;
  var GAP = 70;
  function elevation(tier) { return (tierCount - 1 - tier) * GAP - ((tierCount - 1) * GAP) / 2; }
  function tierColor(tier) { return PALETTE[tier % PALETTE.length]; }

  var nodes = data.nodes.map(function (n) { return Object.assign({}, n); });
  var links = (data.links || []).map(function (l) { return Object.assign({}, l); });
  var G;
  try {
    // Pin elevation up front so the first layout tick already stacks the tiers.
    nodes.forEach(function (n) { n.fy = elevation(n.tier); });
    G = ForceGraph3D()(canvas)
      .backgroundColor(cssVar("--diagram-bg") || "#ffffff")
      .width(canvas.clientWidth || root.clientWidth || 800)
      .height(canvas.clientHeight || 500)
      .nodeLabel(function (n) { return n.label + " · " + (data.tiers[n.tier] || "tier " + n.tier) + " · " + n.files + " file(s)"; })
      .nodeColor(function (n) { return tierColor(n.tier); })
      .nodeVal(function (n) { return 1 + (n.files || 0); })
      .nodeOpacity(0.95)
      .linkColor(function (l) { return l.against ? DANGER : MUTED; })
      .linkWidth(function (l) { return l.against ? 2.5 : 1; })
      .linkOpacity(0.5)
      .linkDirectionalArrowLength(3.5).linkDirectionalArrowRelPos(1)
      .linkDirectionalParticles(function (l) { return l.against ? 3 : 0; })
      .linkDirectionalParticleWidth(2)
      .warmupTicks(40)
      .graphData({ nodes: nodes, links: links });
  } catch (err) {
    fail("The 3D layered view could not start (" + (err && err.message ? err.message : err) + ").");
    return;
  }

  // Size the renderer to the container (ForceGraph3D does not auto-size). Re-measure on the next
  // frame in case layout wasn't settled at init, and on window resize.
  function resize() {
    var w = canvas.clientWidth || root.clientWidth, h = canvas.clientHeight;
    if (w > 0 && h > 0) { G.width(w).height(h); }
  }
  resize();
  requestAnimationFrame(resize);
  setTimeout(resize, 300);
  window.addEventListener("resize", resize);

  function frame(ms) {
    try { G.cameraPosition({ x: 0, y: GAP * tierCount * 0.4, z: GAP * tierCount * 1.7 }, { x: 0, y: 0, z: 0 }, ms); } catch (e) { }
  }
  setTimeout(function () { frame(0); }, 250);

  var reset = document.getElementById("layered3d-reset");
  if (reset) { reset.addEventListener("click", function () { frame(500); }); }
})();
