// The home page background: a dark arena with two slowly swaying spotlights, the outline
// of the octagon on the floor, and dust drifting up through the light. Visitors who ask
// for reduced motion get a single still frame.
(() => {
  const canvas = document.getElementById("arena-canvas");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const CYAN = "79,195,232";    // --accent
  const AMBER = "232,165,79";   // --chart-ko
  let width = 0, height = 0, motes = [], cones = {}, last = 0;

  function mote(y) {
    return {
      x: Math.random() * width,
      y,
      radius: 0.5 + Math.random() * 1.4,
      rise: 5 + Math.random() * 12,          // pixels per second
      phase: Math.random() * Math.PI * 2,
      color: Math.random() < 0.8 ? CYAN : AMBER,
      alpha: 0.12 + Math.random() * 0.3,
    };
  }

  function resize() {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    width = canvas.clientWidth;
    height = canvas.clientHeight;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    const count = Math.round(Math.min(160, (width * height) / 9000));
    motes = Array.from({ length: count }, () => mote(Math.random() * height));
    cones = { cyan: cone(CYAN, 0.16, ratio), amber: cone(AMBER, 0.09, ratio) };
  }

  // A cone of light from above the top edge, drawn once per size onto its own canvas:
  // many faint nested cones, so it's brightest in the middle and fades out at the edges,
  // and dimmer with distance from its source.
  function cone(color, strength, ratio) {
    const half = width * 0.34;
    const layer = document.createElement("canvas");
    layer.width = Math.max(1, Math.round(2 * half * ratio));
    layer.height = Math.max(1, Math.round(height * ratio));
    const c = layer.getContext("2d");
    c.setTransform(ratio, 0, 0, ratio, 0, 0);
    const top = -height * 0.15;
    const steps = 14;
    for (let i = 1; i <= steps; i++) {
      const spread = (half * i) / steps;
      const source = 12 + (28 * i) / steps;
      const glow = c.createRadialGradient(half, top, 0, half, top, height * 1.2);
      glow.addColorStop(0, `rgba(${color},${strength / steps})`);
      glow.addColorStop(1, `rgba(${color},0)`);
      c.fillStyle = glow;
      c.beginPath();
      c.moveTo(half - source, 0);
      c.lineTo(half - spread, height);
      c.lineTo(half + spread, height);
      c.lineTo(half + source, 0);
      c.closePath();
      c.fill();
    }
    return { layer, half };
  }

  function spotlight(x, { layer, half }) {
    ctx.drawImage(layer, x - half, 0, 2 * half, height);
  }

  // The octagon seen at an angle: eight sides, squashed vertically.
  function octagon(cx, cy, radius, alpha) {
    ctx.strokeStyle = `rgba(${CYAN},${alpha})`;
    ctx.lineWidth = 1.25;
    ctx.beginPath();
    for (let i = 0; i <= 8; i++) {
      const angle = Math.PI / 8 + (i * Math.PI) / 4;
      const x = cx + radius * Math.cos(angle);
      const y = cy + radius * 0.38 * Math.sin(angle);
      if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    }
    ctx.stroke();
  }

  function frame(now) {
    const dt = last ? Math.min((now - last) / 1000, 0.05) : 0;
    last = now;
    const t = now / 1000;
    ctx.clearRect(0, 0, width, height);

    spotlight(width * (0.3 + 0.06 * Math.sin(t * 0.13)), cones.cyan);
    spotlight(width * (0.7 + 0.06 * Math.sin(t * 0.1 + 2)), cones.amber);

    const floor = Math.min(width, 1400) * 0.44;
    octagon(width / 2, height * 0.8, floor * (1 + 0.012 * Math.sin(t * 0.5)), 0.12);
    octagon(width / 2, height * 0.8, floor * 0.78, 0.05);

    for (const m of motes) {
      m.y -= m.rise * dt;
      m.x += Math.sin(t * 0.4 + m.phase) * 5 * dt;
      if (m.y < -4) Object.assign(m, mote(height + 4));
      ctx.fillStyle = `rgba(${m.color},${m.alpha})`;
      ctx.beginPath();
      ctx.arc(m.x, m.y, m.radius, 0, Math.PI * 2);
      ctx.fill();
    }
    if (!still) requestAnimationFrame(frame);
  }

  let resizeTimer;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      resize();
      if (still) frame(performance.now());
    }, 150);
  });
  resize();
  requestAnimationFrame(frame);
})();
