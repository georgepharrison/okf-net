/*
 * okf site — the whole client. No third-party JavaScript, no network requests.
 *
 * Three independent features, each initialised only if its markup is on the page:
 *   1. the trust dashboard  (#concept-list) — tiles are filters over OKF_SITE.concepts
 *   2. the graph            (#graph-canvas) — a hand-rolled force-directed layout
 *   3. single-file routing  (#article-host) — swaps pre-rendered articles on hashchange
 *
 * The dashboard degrades: the concept list is rendered into the HTML by the
 * generator, so a reader with JavaScript off still sees every concept, every tile
 * count and every link. This file only adds filtering on top.
 */
(function () {
  "use strict";

  var SITE = window.OKF_SITE || { concepts: [], edges: [], bundles: [], counts: {} };
  var css = getComputedStyle(document.documentElement);

  function token(name, fallback) {
    var value = css.getPropertyValue(name);
    return value ? value.trim() : fallback;
  }

  var TIER_TONE = {
    "human-reviewed": "--good",
    "machine-confirmed": "--warning",
    "unverified": "--neutral"
  };

  /* ------------------------------------------------------------------ *
   * Hash state. One grammar for every page, so a filtered dashboard and
   * a single-file concept view are both linkable and both survive reload
   * from a file:// URL, where there is no server to hold state.
   *   #c=<concept id>       single-file: show this concept
   *   #f=<tile>&b=<bundle>&q=<text>   dashboard filter
   * ------------------------------------------------------------------ */

  function readHash() {
    var raw = window.location.hash.replace(/^#/, "");
    var state = { c: "", f: "", b: "", q: "" };
    if (!raw) return state;
    raw.split("&").forEach(function (part) {
      var eq = part.indexOf("=");
      if (eq < 0) return;
      var key = part.slice(0, eq);
      if (key in state) {
        try {
          state[key] = decodeURIComponent(part.slice(eq + 1).replace(/\+/g, " "));
        } catch (e) {
          state[key] = "";
        }
      }
    });
    return state;
  }

  function writeHash(state, replace) {
    var parts = [];
    ["c", "f", "b", "q"].forEach(function (key) {
      if (state[key]) parts.push(key + "=" + encodeURIComponent(state[key]));
    });
    var hash = parts.length ? "#" + parts.join("&") : "#";
    if (replace && window.history && window.history.replaceState) {
      window.history.replaceState(null, "", hash);
    } else {
      window.location.hash = hash;
    }
  }

  /* ------------------------------------------------------------------ *
   * 1. Trust dashboard
   * ------------------------------------------------------------------ */

  var PREDICATES = {
    all: function () { return true; },
    bundles: function () { return true; },
    "human-reviewed": function (c) { return c.tier === "human-reviewed"; },
    "machine-confirmed": function (c) { return c.tier === "machine-confirmed"; },
    unverified: function (c) { return c.tier === "unverified"; },
    stale: function (c) { return c.stale === true; },
    draft: function (c) { return c.status === "draft"; }
  };

  function initDashboard() {
    var list = document.getElementById("concept-list");
    if (!list) return;

    var tiles = Array.prototype.slice.call(document.querySelectorAll(".tile[data-filter]"));
    var search = document.getElementById("concept-search");
    var readout = document.getElementById("filter-state");
    var drill = document.getElementById("bundle-drill");
    var drillChips = drill ? Array.prototype.slice.call(drill.querySelectorAll(".chip[data-bundle]")) : [];
    var items = Array.prototype.slice.call(list.querySelectorAll("li[data-id]"));
    var byId = {};
    SITE.concepts.forEach(function (c) { byId[c.id] = c; });

    var state = readHash();
    if (!state.f) state.f = "all";
    if (search) search.value = state.q;

    function matches(concept, text) {
      var predicate = PREDICATES[state.f] || PREDICATES.all;
      if (!predicate(concept)) return false;
      if (state.b && concept.bundle !== state.b) return false;
      if (!text) return true;
      var hay = (concept.title + " " + concept.id + " " + (concept.type || "") + " " +
        (concept.description || "") + " " + (concept.tags || []).join(" ")).toLowerCase();
      return hay.indexOf(text) >= 0;
    }

    function apply(push) {
      var text = (state.q || "").trim().toLowerCase();
      var shown = 0;
      items.forEach(function (li) {
        var concept = byId[li.getAttribute("data-id")];
        var visible = concept ? matches(concept, text) : false;
        li.hidden = !visible;
        if (visible) shown++;
      });

      tiles.forEach(function (tile) {
        tile.setAttribute("aria-pressed", tile.getAttribute("data-filter") === state.f ? "true" : "false");
      });
      drillChips.forEach(function (chip) {
        chip.setAttribute("aria-pressed", chip.getAttribute("data-bundle") === state.b ? "true" : "false");
      });
      if (drill) {
        drill.classList.toggle("open", state.f === "bundles" || !!state.b);
      }
      if (readout) {
        readout.innerHTML = "";
        var b = document.createElement("b");
        b.textContent = String(shown);
        readout.appendChild(b);
        readout.appendChild(document.createTextNode(
          " of " + SITE.concepts.length + " concepts" + describe()));
      }
      var empty = document.getElementById("concept-empty");
      if (empty) empty.hidden = shown !== 0;
      // Never clobber a single-file concept route with the dashboard's own filter
      // state: `#c=<id>` is the only hash key that decides which view is on screen.
      if (!readHash().c) {
        writeHash({ c: "", f: state.f === "all" ? "" : state.f, b: state.b, q: state.q }, !push);
      }
    }

    function describe() {
      var bits = [];
      if (state.f && state.f !== "all" && state.f !== "bundles") bits.push(state.f.replace(/-/g, " "));
      if (state.b) bits.push("in " + state.b);
      if ((state.q || "").trim()) bits.push('matching "' + state.q.trim() + '"');
      return bits.length ? " — " + bits.join(", ") : "";
    }

    tiles.forEach(function (tile) {
      tile.addEventListener("click", function () {
        var wanted = tile.getAttribute("data-filter");
        state.f = state.f === wanted ? "all" : wanted;
        if (state.f !== "bundles" && state.f !== "all") state.b = state.b;
        apply(true);
      });
    });

    drillChips.forEach(function (chip) {
      chip.addEventListener("click", function () {
        var wanted = chip.getAttribute("data-bundle");
        state.b = state.b === wanted ? "" : wanted;
        apply(true);
      });
    });

    if (search) {
      search.addEventListener("input", function () {
        state.q = search.value;
        apply(false);
      });
    }

    var reset = document.getElementById("filter-reset");
    if (reset) {
      reset.addEventListener("click", function () {
        state.f = "all";
        state.b = "";
        state.q = "";
        if (search) search.value = "";
        apply(true);
      });
    }

    apply(false);
  }

  /* ------------------------------------------------------------------ *
   * 2. Force-directed graph, on a canvas.
   *
   * A spring-electrical layout (Fruchterman-Reingold with a cooling
   * schedule) run to convergence once, up front, then drawn statically.
   * Nodes are coloured by trust tier by default -- the judgement Ringo
   * wants to read off the graph -- with concept `type` as a toggle.
   * ------------------------------------------------------------------ */

  function initGraph() {
    var canvas = document.getElementById("graph-canvas");
    if (!canvas) return;
    var ctx = canvas.getContext("2d");
    if (!ctx) return;

    var tip = document.getElementById("graph-tip");
    var legend = document.getElementById("graph-legend");
    var colorMode = "tier";

    var nodes = SITE.concepts.map(function (c, i) {
      return {
        id: c.id, title: c.title, type: c.type || "—", tier: c.tier,
        stale: !!c.stale, status: c.status, href: c.href, bundle: c.bundle,
        degree: 0, index: i, x: 0, y: 0, vx: 0, vy: 0
      };
    });
    var index = {};
    nodes.forEach(function (n) { index[n.id] = n; });

    var links = [];
    (SITE.edges || []).forEach(function (edge) {
      var a = index[edge[0]];
      var b = index[edge[1]];
      if (!a || !b || a === b) return;
      links.push({ source: a, target: b });
      a.degree++;
      b.degree++;
    });

    var types = [];
    nodes.forEach(function (n) { if (types.indexOf(n.type) < 0) types.push(n.type); });
    types.sort();

    function typeColor(type) {
      var slot = types.indexOf(type);
      if (slot < 0 || slot > 7) return token("--neutral", "#898781");
      return token("--cat-" + (slot + 1), "#2a78d6");
    }

    function nodeColor(n) {
      if (colorMode === "type") return typeColor(n.type);
      return token(TIER_TONE[n.tier] || "--neutral", "#898781");
    }

    function radius(n) { return 6 + Math.min(12, n.degree * 1.6); }

    /* --- layout --- */
    function layout() {
      var n = nodes.length;
      if (n === 0) return;
      // Lay out in the shape of the canvas the result will be drawn on, so a wide
      // screen gets a wide graph instead of a square one letterboxed into it.
      var box = canvas.getBoundingClientRect();
      var width = Math.max(600, Math.round(box.width) || 1000);
      var height = Math.max(400, Math.round(box.height) || 700);
      var area = width * height;
      var k = Math.sqrt(area / n);
      // Deterministic start: the same bundle always lays out the same way, so a
      // regenerated site is not a diff of random jitter.
      var seed = 1;
      function random() {
        seed = (seed * 1103515245 + 12345) % 2147483648;
        return seed / 2147483648;
      }
      nodes.forEach(function (node, i) {
        var angle = (i / n) * Math.PI * 2;
        node.x = width / 2 + Math.cos(angle) * (k * 2 + random() * k);
        node.y = height / 2 + Math.sin(angle) * (k * 2 + random() * k);
      });

      var iterations = n > 300 ? 160 : 400;
      var temperature = width / 8;
      var cooling = temperature / (iterations + 1);

      for (var step = 0; step < iterations; step++) {
        for (var i = 0; i < n; i++) { nodes[i].vx = 0; nodes[i].vy = 0; }

        for (i = 0; i < n; i++) {
          for (var j = i + 1; j < n; j++) {
            var a = nodes[i];
            var b = nodes[j];
            var dx = a.x - b.x;
            var dy = a.y - b.y;
            var d2 = dx * dx + dy * dy;
            if (d2 < 0.01) { dx = (i - j) * 0.01 + 0.01; dy = 0.01; d2 = dx * dx + dy * dy; }
            var d = Math.sqrt(d2);
            var repel = (k * k) / d;
            var ux = dx / d;
            var uy = dy / d;
            a.vx += ux * repel; a.vy += uy * repel;
            b.vx -= ux * repel; b.vy -= uy * repel;
          }
        }

        for (var e = 0; e < links.length; e++) {
          var s = links[e].source;
          var t = links[e].target;
          var ex = s.x - t.x;
          var ey = s.y - t.y;
          var ed = Math.sqrt(ex * ex + ey * ey) || 0.01;
          var attract = (ed * ed) / k;
          var ax = (ex / ed) * attract;
          var ay = (ey / ed) * attract;
          s.vx -= ax; s.vy -= ay;
          t.vx += ax; t.vy += ay;
        }

        for (i = 0; i < n; i++) {
          var node = nodes[i];
          // A weak pull to the centre keeps disconnected concepts -- of which an
          // honest bundle has many -- from drifting off the canvas entirely.
          node.vx += (width / 2 - node.x) * 0.012 * k * 0.1;
          node.vy += (height / 2 - node.y) * 0.012 * k * 0.1;
          var speed = Math.sqrt(node.vx * node.vx + node.vy * node.vy) || 1;
          var limit = Math.min(speed, temperature);
          node.x += (node.vx / speed) * limit;
          node.y += (node.vy / speed) * limit;
        }
        temperature -= cooling;
      }
    }

    /* --- view transform --- */
    var view = { scale: 1, x: 0, y: 0 };

    function fit() {
      if (!nodes.length) { view = { scale: 1, x: 0, y: 0 }; return; }
      var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      nodes.forEach(function (n) {
        minX = Math.min(minX, n.x); maxX = Math.max(maxX, n.x);
        minY = Math.min(minY, n.y); maxY = Math.max(maxY, n.y);
      });
      var pad = 60;
      var w = Math.max(1, maxX - minX);
      var h = Math.max(1, maxY - minY);
      var rect = canvas.getBoundingClientRect();
      view.scale = Math.min((rect.width - pad * 2) / w, (rect.height - pad * 2) / h);
      if (!isFinite(view.scale) || view.scale <= 0) view.scale = 1;
      view.scale = Math.min(view.scale, 2.2);
      view.x = rect.width / 2 - ((minX + maxX) / 2) * view.scale;
      view.y = rect.height / 2 - ((minY + maxY) / 2) * view.scale;
    }

    function toScreen(n) {
      return { x: n.x * view.scale + view.x, y: n.y * view.scale + view.y };
    }

    function resize() {
      var rect = canvas.getBoundingClientRect();
      var dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.round(rect.width * dpr));
      canvas.height = Math.max(1, Math.round(rect.height * dpr));
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    var hovered = null;

    function draw() {
      var rect = canvas.getBoundingClientRect();
      ctx.clearRect(0, 0, rect.width, rect.height);

      ctx.strokeStyle = token("--rule", "#c3c2b7");
      ctx.globalAlpha = 0.55;
      ctx.lineWidth = 1;
      links.forEach(function (link) {
        var a = toScreen(link.source);
        var b = toScreen(link.target);
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        ctx.lineTo(b.x, b.y);
        ctx.stroke();
      });
      ctx.globalAlpha = 1;

      var surface = token("--surface-1", "#fcfcfb");
      var ink = token("--ink", "#0b0b0b");
      var showLabels = view.scale > 0.55 || nodes.length < 60;

      nodes.forEach(function (n) {
        var p = toScreen(n);
        var r = radius(n) * Math.min(1.4, Math.max(0.7, view.scale));
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
        ctx.fillStyle = nodeColor(n);
        ctx.globalAlpha = n.status === "deprecated" ? 0.5 : 1;
        ctx.fill();
        // A 2px surface ring keeps overlapping nodes readable.
        ctx.lineWidth = 2;
        ctx.strokeStyle = surface;
        ctx.stroke();
        if (n.stale) {
          ctx.beginPath();
          ctx.arc(p.x, p.y, r + 3.5, 0, Math.PI * 2);
          ctx.setLineDash([3, 3]);
          ctx.lineWidth = 1.6;
          ctx.strokeStyle = token("--critical", "#d03b3b");
          ctx.stroke();
          ctx.setLineDash([]);
        }
        if (n === hovered) {
          ctx.beginPath();
          ctx.arc(p.x, p.y, r + 6, 0, Math.PI * 2);
          ctx.lineWidth = 2;
          ctx.strokeStyle = token("--accent", "#2a78d6");
          ctx.stroke();
        }
        ctx.globalAlpha = 1;
        if (showLabels) {
          ctx.fillStyle = ink;
          ctx.font = "11px " + token("--font", "sans-serif");
          ctx.textAlign = "center";
          ctx.textBaseline = "top";
          var label = n.title.length > 26 ? n.title.slice(0, 25) + "…" : n.title;
          ctx.fillText(label, p.x, p.y + r + 4);
        }
      });
    }

    function pick(px, py) {
      for (var i = nodes.length - 1; i >= 0; i--) {
        var p = toScreen(nodes[i]);
        var r = radius(nodes[i]) * Math.min(1.4, Math.max(0.7, view.scale)) + 6;
        var dx = px - p.x;
        var dy = py - p.y;
        if (dx * dx + dy * dy <= r * r) return nodes[i];
      }
      return null;
    }

    function renderLegend() {
      if (!legend) return;
      legend.innerHTML = "";
      var entries = colorMode === "tier"
        ? [["human-reviewed", token("--good", "#0ca30c")],
           ["machine-confirmed", token("--warning", "#fab219")],
           ["unverified", token("--neutral", "#898781")]]
        : types.slice(0, 8).map(function (t) { return [t, typeColor(t)]; });
      entries.forEach(function (entry) {
        var key = document.createElement("span");
        key.className = "key";
        var swatch = document.createElement("span");
        swatch.className = "swatch";
        swatch.style.background = entry[1];
        key.appendChild(swatch);
        key.appendChild(document.createTextNode(entry[0]));
        legend.appendChild(key);
      });
      if (colorMode === "type" && types.length > 8) {
        var more = document.createElement("span");
        more.className = "key";
        more.textContent = "+" + (types.length - 8) + " more (grey)";
        legend.appendChild(more);
      }
      var stale = document.createElement("span");
      stale.className = "key";
      stale.textContent = "◌ dashed ring = stale";
      legend.appendChild(stale);
    }

    /* --- interaction --- */
    var dragging = null;
    var panning = false;
    var last = { x: 0, y: 0 };
    var moved = false;

    function local(event) {
      var rect = canvas.getBoundingClientRect();
      return { x: event.clientX - rect.left, y: event.clientY - rect.top };
    }

    canvas.addEventListener("pointerdown", function (event) {
      var p = local(event);
      moved = false;
      dragging = pick(p.x, p.y);
      panning = !dragging;
      last = p;
      canvas.classList.add("dragging");
      canvas.setPointerCapture(event.pointerId);
    });

    canvas.addEventListener("pointermove", function (event) {
      var p = local(event);
      if (dragging) {
        moved = true;
        dragging.x += (p.x - last.x) / view.scale;
        dragging.y += (p.y - last.y) / view.scale;
        last = p;
        draw();
        return;
      }
      if (panning) {
        moved = true;
        view.x += p.x - last.x;
        view.y += p.y - last.y;
        last = p;
        draw();
        return;
      }
      var over = pick(p.x, p.y);
      if (over !== hovered) {
        hovered = over;
        draw();
      }
      if (tip) {
        if (over) {
          tip.style.display = "block";
          tip.textContent = over.title + " · " + over.type + " · " + over.tier +
            (over.stale ? " · stale" : "");
          tip.style.left = Math.min(p.x + 14, canvas.clientWidth - 200) + "px";
          tip.style.top = (p.y + 14) + "px";
        } else {
          tip.style.display = "none";
        }
      }
    });

    function release(event) {
      if (dragging && !moved) {
        window.location.href = dragging.href;
      }
      dragging = null;
      panning = false;
      canvas.classList.remove("dragging");
      if (event && event.pointerId !== undefined && canvas.hasPointerCapture(event.pointerId)) {
        canvas.releasePointerCapture(event.pointerId);
      }
    }

    canvas.addEventListener("pointerup", release);
    canvas.addEventListener("pointercancel", release);
    canvas.addEventListener("pointerleave", function () {
      if (tip) tip.style.display = "none";
      hovered = null;
      draw();
    });

    canvas.addEventListener("wheel", function (event) {
      event.preventDefault();
      var p = local(event);
      var factor = Math.exp(-event.deltaY * 0.0015);
      var next = Math.min(4, Math.max(0.15, view.scale * factor));
      view.x = p.x - (p.x - view.x) * (next / view.scale);
      view.y = p.y - (p.y - view.y) * (next / view.scale);
      view.scale = next;
      draw();
    }, { passive: false });

    var refit = document.getElementById("graph-fit");
    if (refit) refit.addEventListener("click", function () { fit(); draw(); });

    var relayout = document.getElementById("graph-relayout");
    if (relayout) relayout.addEventListener("click", function () { layout(); fit(); draw(); });

    var toggle = document.getElementById("graph-color");
    if (toggle) {
      toggle.addEventListener("click", function () {
        colorMode = colorMode === "tier" ? "type" : "tier";
        toggle.textContent = colorMode === "tier" ? "Colour by type" : "Colour by trust tier";
        renderLegend();
        draw();
      });
    }

    // In single-file mode the graph starts inside a hidden view, so its canvas has
    // no size to fit against yet. Fit on the first resize that gives it one.
    var fitted = false;

    window.addEventListener("resize", function () {
      resize();
      if (!fitted && canvas.getBoundingClientRect().width > 0) {
        fit();
        fitted = true;
      }
      draw();
    });

    resize();
    layout();
    fitted = canvas.getBoundingClientRect().width > 0;
    fit();
    renderLegend();
    draw();
  }

  /* ------------------------------------------------------------------ *
   * 3. Single-file routing. Every concept's article is already in the
   *    document as an inert <template>-like script payload; the router
   *    swaps one into the host element on #c=<id>.
   * ------------------------------------------------------------------ */

  function initRouter() {
    var host = document.getElementById("article-host");
    if (!host) return;
    var payload = document.getElementById("okf-articles");
    if (!payload) return;

    var articles;
    try {
      articles = JSON.parse(payload.textContent || "{}");
    } catch (e) {
      return;
    }

    var views = Array.prototype.slice.call(document.querySelectorAll(".view"));

    function show(name) {
      views.forEach(function (view) {
        view.classList.toggle("active", view.getAttribute("data-view") === name);
      });
    }

    function route() {
      var state = readHash();
      if (state.c && Object.prototype.hasOwnProperty.call(articles, state.c)) {
        host.innerHTML = articles[state.c];
        show("concept");
        window.scrollTo(0, 0);
      } else {
        show("dashboard");
        // The graph canvas may have been sized while hidden; let it re-fit.
        window.dispatchEvent(new Event("resize"));
      }
    }

    window.addEventListener("hashchange", route);
    route();
  }

  function ready() {
    initDashboard();
    initGraph();
    initRouter();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", ready);
  } else {
    ready();
  }
})();
