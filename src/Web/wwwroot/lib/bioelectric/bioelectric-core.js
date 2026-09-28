/**
 * BioElectric Hero Background - Shared Core
 * Animated canvas visualizing bio-electric therapy technology using Voronoi/Delaunay diagrams.
 */

// Configuration constants
const ENERGY_RETENTION = 0.92;
const DEATH_THRESHOLD = 0.06;
const ACTIVATION_BOOST_START = 0.7;
const ACTIVATION_BOOST_END = 0.6;
const MAX_ALPHA = 0.33;
const EDGE_MARGIN = 50;
const HEARTBEAT_INTERVAL = 950; // ~63 BPM in milliseconds
const MOBILE_BREAKPOINT = 768;
const POINT_COUNT_DESKTOP = 250;
const POINT_COUNT_MOBILE = 175;
const RESIZE_DEBOUNCE_MS = 150;

class Point {
  constructor(x, y) {
    this.baseX = x;
    this.baseY = y;
    this.x = x;
    this.y = y;
    this.breathPhase = Math.random() * Math.PI * 2;
    this.breathSpeed = 0.0001 + Math.random() * 0.0002;
    this.breathAmplitudeX = 4 + Math.random() * 10;
    this.breathAmplitudeY = 4 + Math.random() * 10;
    this.breathPhase2 = Math.random() * Math.PI * 2;
    this.breathSpeed2 = 0.00005 + Math.random() * 0.0001;
    this.glowPhase = Math.random() * Math.PI * 2;
    this.activation = 0; // Healing activation level (0-1)
  }

  update(time) {
    const breath1 = Math.sin(time * this.breathSpeed + this.breathPhase);
    const breath2 = Math.sin(time * this.breathSpeed2 + this.breathPhase2) * 0.5;

    // Size boost from activation - cells "swell" when activated
    const sizeBoost = 1 + this.activation * 0.3;

    this.x = this.baseX + (breath1 + breath2) * this.breathAmplitudeX * sizeBoost;
    this.y = this.baseY + Math.cos(time * this.breathSpeed * 0.8 + this.breathPhase) * this.breathAmplitudeY * sizeBoost
                       + Math.cos(time * this.breathSpeed2 + this.breathPhase2) * this.breathAmplitudeY * 0.3 * sizeBoost;

    // Exponential decay: rapid initial drop, gradual tail (~10 second overall duration)
    const decayRate = 0.003 + this.activation * 0.015;
    this.activation -= this.activation * decayRate;
    if (this.activation < 0.005) this.activation = 0;
  }
}

class Pulse {
  constructor(startIdx, endIdx, width, height, hopCount = 0, intensity = 1) {
    this.startIdx = startIdx;
    this.endIdx = endIdx;
    this.width = width;
    this.height = height;
    this.progress = 0;
    this.speed = 0.035 + Math.random() * 0.015;
    this.alive = true;
    this.intensity = intensity;
    this.hopCount = hopCount;
    this.activatedStart = false;
  }

  isNearEdge(pt) {
    return pt.x < EDGE_MARGIN || pt.x > this.width - EDGE_MARGIN ||
           pt.y < EDGE_MARGIN || pt.y > this.height - EDGE_MARGIN;
  }

  update(delaunay, pts, pulseArray) {
    // Activate start cell immediately - stronger boost
    if (!this.activatedStart) {
      pts[this.startIdx].activation = Math.min(1, pts[this.startIdx].activation + this.intensity * ACTIVATION_BOOST_START);
      this.activatedStart = true;
    }

    // Continuously activate cells along the path as we travel
    const currentPos = this.progress;
    if (currentPos > 0.3 && currentPos < 0.7) {
      pts[this.endIdx].activation = Math.min(1, pts[this.endIdx].activation + this.intensity * 0.04);
    }

    this.progress += this.speed;

    if (this.progress >= 1) {
      // Activate destination cell on arrival - stronger boost
      pts[this.endIdx].activation = Math.min(1, pts[this.endIdx].activation + this.intensity * ACTIVATION_BOOST_END);

      if (this.isNearEdge(pts[this.endIdx])) {
        this.alive = false;
        return;
      }

      this.hopCount++;

      const currStart = pts[this.startIdx];
      const currEnd = pts[this.endIdx];
      const travelVecX = currEnd.x - currStart.x;
      const travelVecY = currEnd.y - currStart.y;
      const travelAngle = Math.atan2(travelVecY, travelVecX);

      const neighbors = [...delaunay.neighbors(this.endIdx)];
      const validNeighbors = neighbors.filter(n => n !== this.startIdx);

      if (validNeighbors.length === 0) {
        this.alive = false;
        return;
      }

      const neighborsWithAngles = validNeighbors.map(n => {
        const neighbor = pts[n];
        const vecX = neighbor.x - currEnd.x;
        const vecY = neighbor.y - currEnd.y;
        const angle = Math.atan2(vecY, vecX);
        let deviation = Math.abs(angle - travelAngle);
        if (deviation > Math.PI) deviation = 2 * Math.PI - deviation;
        return { idx: n, deviation };
      });

      neighborsWithAngles.sort((a, b) => a.deviation - b.deviation);

      // Only continue in forward-ish directions
      const forwardNeighbors = neighborsWithAngles.filter(n => n.deviation < Math.PI / 2);

      if (forwardNeighbors.length === 0) {
        this.alive = false;
        return;
      }

      // Energy model: travel cost then divide among paths
      const afterTravelIntensity = this.intensity * ENERGY_RETENTION;
      const energyPerPath = afterTravelIntensity / forwardNeighbors.length;

      // Die if too weak
      if (energyPerPath < DEATH_THRESHOLD) {
        this.alive = false;
        return;
      }

      // Spawn pulses for additional forward paths
      for (let i = 1; i < forwardNeighbors.length; i++) {
        pulseArray.push(new Pulse(
          this.endIdx,
          forwardNeighbors[i].idx,
          this.width,
          this.height,
          this.hopCount,
          energyPerPath
        ));
      }

      // Continue on primary (most forward) path
      const nextIdx = forwardNeighbors[0].idx;
      this.startIdx = this.endIdx;
      this.endIdx = nextIdx;
      this.progress = 0;
      this.intensity = energyPerPath;
    }
  }
}

/**
 * Creates and manages the BioElectric animation
 * @param {HTMLCanvasElement} canvas - The canvas element to draw on
 * @param {Object} options - Configuration options
 * @param {boolean} options.useContainerSize - If true, size to parent container; if false, size to window
 */
function createBioElectricAnimation(canvas, options = {}) {
  const useContainerSize = options.useContainerSize || false;

  const ctx = canvas.getContext('2d');
  if (!ctx) {
    console.error('BioElectric: Failed to get 2D canvas context');
    return;
  }

  let width, height;
  let points = [];
  let pulses = [];
  let animationId = null;
  let lastBurstTime = 0;
  let resizeTimeout = null;
  let pointCount;

  function getPointCount() {
    const checkWidth = useContainerSize ? canvas.parentElement?.offsetWidth : window.innerWidth;
    return (checkWidth || window.innerWidth) < MOBILE_BREAKPOINT ? POINT_COUNT_MOBILE : POINT_COUNT_DESKTOP;
  }

  function initPoints() {
    points = [];
    pointCount = getPointCount();
    const cols = Math.ceil(Math.sqrt(pointCount * (width / height)));
    const rows = Math.ceil(pointCount / cols);
    const cellW = width / cols;
    const cellH = height / rows;

    for (let i = 0; i < pointCount; i++) {
      const col = i % cols;
      const row = Math.floor(i / cols);
      const x = (col + 0.5) * cellW + (Math.random() - 0.5) * cellW * 0.9;
      const y = (row + 0.5) * cellH + (Math.random() - 0.5) * cellH * 0.9;
      points.push(new Point(
        Math.max(0, Math.min(width, x)),
        Math.max(0, Math.min(height, y))
      ));
    }
  }

  function resize() {
    if (useContainerSize) {
      const container = canvas.parentElement;
      width = canvas.width = container.offsetWidth;
      height = canvas.height = container.offsetHeight;
    } else {
      width = canvas.width = window.innerWidth;
      height = canvas.height = window.innerHeight;
    }
    initPoints();
    pulses = []; // Clear pulses on resize to avoid dimension mismatch
  }

  function debouncedResize() {
    clearTimeout(resizeTimeout);
    resizeTimeout = setTimeout(resize, RESIZE_DEBOUNCE_MS);
  }

  function spawnBurst(delaunay) {
    if (points.length === 0) return;

    let attempts = 0;
    let originIdx;
    do {
      originIdx = Math.floor(Math.random() * points.length);
      attempts++;
    } while (
      attempts < 50 &&
      (points[originIdx].x < EDGE_MARGIN * 2 ||
       points[originIdx].x > width - EDGE_MARGIN * 2 ||
       points[originIdx].y < EDGE_MARGIN * 2 ||
       points[originIdx].y > height - EDGE_MARGIN * 2)
    );

    const neighbors = [...delaunay.neighbors(originIdx)];

    // Activate the origin cell
    points[originIdx].activation = Math.min(1, points[originIdx].activation + 0.5);

    neighbors.forEach(neighborIdx => {
      pulses.push(new Pulse(originIdx, neighborIdx, width, height, 0, 1));
    });
  }

  function draw(time) {
    const gradient = ctx.createRadialGradient(
      width * 0.3, height * 0.4, 0,
      width * 0.5, height * 0.5, width * 0.9
    );
    gradient.addColorStop(0, '#0a1628');
    gradient.addColorStop(0.5, '#061018');
    gradient.addColorStop(1, '#020608');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, width, height);

    points.forEach(p => p.update(time));

    const coords = new Float64Array(points.length * 2);
    points.forEach((p, i) => {
      coords[i * 2] = p.x;
      coords[i * 2 + 1] = p.y;
    });

    const delaunay = new d3.Delaunay(coords);
    const voronoi = delaunay.voronoi([0, 0, width, height]);

    // Spawn bursts at consistent heartbeat rate (~63 BPM)
    if (time - lastBurstTime > HEARTBEAT_INTERVAL) {
      spawnBurst(delaunay);
      lastBurstTime = time;
    }

    // Draw Voronoi cells
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    for (let i = 0; i < points.length; i++) {
      const cell = voronoi.cellPolygon(i);
      if (!cell) continue;

      const point = points[i];
      // 1.5x faster dynamics
      const cellPulse = Math.sin(time * point.breathSpeed * 3 + point.glowPhase);
      // Base range: 4-12% opacity
      const baseAlpha = 0.08 + cellPulse * 0.04;
      // Add activation boost, cap total at 33%
      const alpha = Math.min(MAX_ALPHA, baseAlpha + point.activation * 0.28);

      ctx.strokeStyle = `rgba(20, 184, 166, ${alpha})`;
      ctx.lineWidth = 1 + point.activation * 0.8;
      ctx.beginPath();
      ctx.moveTo(cell[0][0], cell[0][1]);
      for (let j = 1; j < cell.length; j++) {
        ctx.lineTo(cell[j][0], cell[j][1]);
      }
      ctx.closePath();
      ctx.stroke();
    }

    // Draw Delaunay edges
    ctx.lineWidth = 1.2;
    const drawnEdges = new Set();

    for (let i = 0; i < points.length; i++) {
      for (const j of delaunay.neighbors(i)) {
        const edgeKey = i < j ? `${i}-${j}` : `${j}-${i}`;
        if (drawnEdges.has(edgeKey)) continue;
        drawnEdges.add(edgeKey);

        const p1 = points[i];
        const p2 = points[j];
        const dist = Math.hypot(p1.x - p2.x, p1.y - p2.y);

        const maxDist = Math.max(width, height) * 0.15;
        const distAlpha = Math.max(0, 1 - dist / maxDist);
        const alpha = distAlpha * 0.12;

        if (alpha < 0.01) continue;

        const edgeGradient = ctx.createLinearGradient(p1.x, p1.y, p2.x, p2.y);
        edgeGradient.addColorStop(0, `rgba(20, 184, 166, ${alpha})`);
        edgeGradient.addColorStop(0.5, `rgba(56, 189, 248, ${alpha * 0.7})`);
        edgeGradient.addColorStop(1, `rgba(192, 132, 252, ${alpha})`);

        ctx.strokeStyle = edgeGradient;
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.stroke();
      }
    }

    // Update pulses (invisible - only their effect on cells is visible)
    pulses = pulses.filter(p => p.alive);
    pulses.forEach(pulse => {
      pulse.update(delaunay, points, pulses);
    });

    animationId = requestAnimationFrame(draw);
  }

  function start() {
    if (animationId) return; // Already running
    resize();
    window.addEventListener('resize', debouncedResize);
    animationId = requestAnimationFrame(draw);
  }

  // Auto-start
  start();
}
