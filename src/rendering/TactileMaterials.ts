import * as THREE from 'three';

/**
 * Генератор процедурных PBR-текстур высокого разрешения (Canvas -> Texture / BumpMap / RoughnessMap)
 * для создания тактильной Stop-Motion / Clay & Felt картинки как на референсах.
 */

const textureCache = new Map<string, { map: THREE.CanvasTexture; bump: THREE.CanvasTexture }>();

/**
 * 1. Текстура паркета «Ёлочка / Доска» (как в Гостиной на референсе №3)
 */
export function createWoodParquetMaterial(tintHex: number = 0x8b4a1e): THREE.MeshPhysicalMaterial {
  const key = `parquet_${tintHex}`;
  let cached = textureCache.get(key);

  if (!cached) {
    const size = 512;
    const colorCanvas = document.createElement('canvas');
    colorCanvas.width = size;
    colorCanvas.height = size;
    const ctx = colorCanvas.getContext('2d')!;

    const bumpCanvas = document.createElement('canvas');
    bumpCanvas.width = size;
    bumpCanvas.height = size;
    const bCtx = bumpCanvas.getContext('2d')!;

    const baseCol = new THREE.Color(tintHex);
    ctx.fillStyle = `#${baseCol.getHexString()}`;
    ctx.fillRect(0, 0, size, size);

    bCtx.fillStyle = '#808080';
    bCtx.fillRect(0, 0, size, size);

    const rows = 8;
    const cols = 4;
    const plankW = size / cols;
    const plankH = size / rows;

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const offsetX = (r % 2) * (plankW * 0.5);
        const x = (c * plankW + offsetX) % size;
        const y = r * plankH;

        // Вариация оттенка каждой плашки паркета
        const shade = (Math.sin(r * 7.3 + c * 13.1) * 0.08);
        const plankColor = baseCol.clone().offsetHSL(0, 0, shade);
        ctx.fillStyle = `#${plankColor.getHexString()}`;
        ctx.fillRect(x, y, plankW, plankH);

        // Продольные волокна древесины
        ctx.strokeStyle = shade > 0 ? 'rgba(60, 25, 5, 0.16)' : 'rgba(255, 200, 140, 0.1)';
        ctx.lineWidth = 1.5;
        for (let g = 0; g < 6; g++) {
          const gy = y + (g + 0.5) * (plankH / 6);
          ctx.beginPath();
          ctx.moveTo(x, gy);
          ctx.bezierCurveTo(x + plankW * 0.33, gy + 2, x + plankW * 0.66, gy - 2, x + plankW, gy);
          ctx.stroke();
        }

        // Фаска / стык между досками (в цвет и в карту рельефа bumpMap)
        ctx.strokeStyle = 'rgba(25, 10, 2, 0.55)';
        ctx.lineWidth = 2.5;
        ctx.strokeRect(x, y, plankW, plankH);

        bCtx.strokeStyle = '#151515';
        bCtx.lineWidth = 3;
        bCtx.strokeRect(x, y, plankW, plankH);
      }
    }

    const map = new THREE.CanvasTexture(colorCanvas);
    map.wrapS = THREE.RepeatWrapping;
    map.wrapT = THREE.RepeatWrapping;
    map.repeat.set(6, 6);
    map.colorSpace = THREE.SRGBColorSpace;

    const bump = new THREE.CanvasTexture(bumpCanvas);
    bump.wrapS = THREE.RepeatWrapping;
    bump.wrapT = THREE.RepeatWrapping;
    bump.repeat.set(6, 6);

    cached = { map, bump };
    textureCache.set(key, cached);
  }

  return new THREE.MeshPhysicalMaterial({
    map: cached.map,
    bumpMap: cached.bump,
    bumpScale: 0.018,
    roughness: 0.38,
    metalness: 0.05,
    clearcoat: 0.25,
    clearcoatRoughness: 0.3
  });
}

/**
 * 2. Глянцевая шахматная кафельная плитка (как на Кухне на референсе №2)
 */
export function createCheckerTileMaterial(): THREE.MeshPhysicalMaterial {
  const key = 'kitchen_checker_tile';
  let cached = textureCache.get(key);

  if (!cached) {
    const size = 512;
    const colorCanvas = document.createElement('canvas');
    colorCanvas.width = size;
    colorCanvas.height = size;
    const ctx = colorCanvas.getContext('2d')!;

    const bumpCanvas = document.createElement('canvas');
    bumpCanvas.width = size;
    bumpCanvas.height = size;
    const bCtx = bumpCanvas.getContext('2d')!;

    const cells = 4;
    const cellS = size / cells;

    bCtx.fillStyle = '#dcdcdc';
    bCtx.fillRect(0, 0, size, size);

    for (let r = 0; r < cells; r++) {
      for (let c = 0; c < cells; c++) {
        const isDark = (r + c) % 2 === 0;
        ctx.fillStyle = isDark ? '#1a202c' : '#e2e8f0';
        ctx.fillRect(c * cellS, r * cellS, cellS, cellS);

        // Легкая мраморная фактура внутри плитки
        ctx.fillStyle = isDark ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.04)';
        ctx.fillRect(c * cellS + 8, r * cellS + 8, cellS - 16, cellS - 16);

        // Шов затирки
        ctx.strokeStyle = '#64748b';
        ctx.lineWidth = 4;
        ctx.strokeRect(c * cellS, r * cellS, cellS, cellS);

        bCtx.strokeStyle = '#202020';
        bCtx.lineWidth = 5;
        bCtx.strokeRect(c * cellS, r * cellS, cellS, cellS);
      }
    }

    const map = new THREE.CanvasTexture(colorCanvas);
    map.wrapS = THREE.RepeatWrapping;
    map.wrapT = THREE.RepeatWrapping;
    map.repeat.set(4, 4);
    map.colorSpace = THREE.SRGBColorSpace;

    const bump = new THREE.CanvasTexture(bumpCanvas);
    bump.wrapS = THREE.RepeatWrapping;
    bump.wrapT = THREE.RepeatWrapping;
    bump.repeat.set(4, 4);

    cached = { map, bump };
    textureCache.set(key, cached);
  }

  return new THREE.MeshPhysicalMaterial({
    map: cached.map,
    bumpMap: cached.bump,
    bumpScale: 0.015,
    roughness: 0.14,
    metalness: 0.08,
    clearcoat: 0.65,
    clearcoatRoughness: 0.15
  });
}

/**
 * 3. Вязаный рождественский ковёр крупной вязки с узором (как на референсе №3)
 */
export function createChristmasRugMaterial(): THREE.MeshPhysicalMaterial {
  const key = 'christmas_knit_rug';
  let cached = textureCache.get(key);

  if (!cached) {
    const size = 512;
    const colorCanvas = document.createElement('canvas');
    colorCanvas.width = size;
    colorCanvas.height = size;
    const ctx = colorCanvas.getContext('2d')!;

    const bumpCanvas = document.createElement('canvas');
    bumpCanvas.width = size;
    bumpCanvas.height = size;
    const bCtx = bumpCanvas.getContext('2d')!;

    // Фон ковра — глубокий рождественский бордово-красный
    ctx.fillStyle = '#88131b';
    ctx.fillRect(0, 0, size, size);

    // Зеленая и кремовая окантовка по краям ковра
    const border = [
      { inset: 14, color: '#f5e6c8', width: 14 },
      { inset: 34, color: '#14532d', width: 22 },
      { inset: 62, color: '#f5e6c8', width: 10 },
      { inset: 82, color: '#991b1b', width: 16 }
    ];
    border.forEach((b) => {
      ctx.strokeStyle = b.color;
      ctx.lineWidth = b.width;
      ctx.strokeRect(b.inset, b.inset, size - b.inset * 2, size - b.inset * 2);
    });

    // Центральный скандинавский ромбовидный медальон
    ctx.save();
    ctx.translate(size / 2, size / 2);
    ctx.rotate(Math.PI / 4);
    ctx.fillStyle = '#14532d';
    ctx.fillRect(-90, -90, 180, 180);
    ctx.strokeStyle = '#f5e6c8';
    ctx.lineWidth = 12;
    ctx.strokeRect(-90, -90, 180, 180);
    ctx.fillStyle = '#991b1b';
    ctx.fillRect(-48, -48, 96, 96);
    ctx.restore();

    // Рельеф петель крупной шерстяной вязки в bumpMap и поверх рисунка
    bCtx.fillStyle = '#505050';
    bCtx.fillRect(0, 0, size, size);

    const step = 8;
    for (let y = 0; y < size; y += step) {
      for (let x = 0; x < size; x += step) {
        bCtx.fillStyle = (x + y) % 16 === 0 ? '#e5e5e5' : '#959595';
        bCtx.beginPath();
        bCtx.arc(x + step / 2, y + step / 2, step * 0.38, 0, Math.PI * 2);
        bCtx.fill();

        ctx.fillStyle = 'rgba(0,0,0,0.08)';
        ctx.fillRect(x, y, 2, 2);
      }
    }

    const map = new THREE.CanvasTexture(colorCanvas);
    map.colorSpace = THREE.SRGBColorSpace;
    const bump = new THREE.CanvasTexture(bumpCanvas);

    cached = { map, bump };
    textureCache.set(key, cached);
  }

  return new THREE.MeshPhysicalMaterial({
    map: cached.map,
    bumpMap: cached.bump,
    bumpScale: 0.04,
    roughness: 0.92,
    metalness: 0.0,
    sheen: 0.75,
    sheenRoughness: 0.8,
    sheenColor: new THREE.Color(0xffedd5)
  });
}

/**
 * 4. Кирпичная кладка для Камина (как на референсе Гостиной №3)
 */
export function createBrickMaterial(baseHex: number = 0x9a3412): THREE.MeshStandardMaterial {
  const key = `brick_${baseHex}`;
  let cached = textureCache.get(key);

  if (!cached) {
    const size = 256;
    const colorCanvas = document.createElement('canvas');
    colorCanvas.width = size;
    colorCanvas.height = size;
    const ctx = colorCanvas.getContext('2d')!;

    const bumpCanvas = document.createElement('canvas');
    bumpCanvas.width = size;
    bumpCanvas.height = size;
    const bCtx = bumpCanvas.getContext('2d')!;

    // Швы раствора
    ctx.fillStyle = '#473c35';
    ctx.fillRect(0, 0, size, size);
    bCtx.fillStyle = '#202020';
    bCtx.fillRect(0, 0, size, size);

    const rows = 8;
    const cols = 4;
    const bw = size / cols;
    const bh = size / rows;
    const baseCol = new THREE.Color(baseHex);

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const ox = (r % 2) * (bw * 0.5);
        const x = (c * bw + ox) % size;
        const y = r * bh;

        const cCol = baseCol.clone().offsetHSL(0, 0, Math.sin(r * 5 + c * 9) * 0.06);
        ctx.fillStyle = `#${cCol.getHexString()}`;
        ctx.fillRect(x + 2, y + 2, bw - 4, bh - 4);

        bCtx.fillStyle = '#d8d8d8';
        bCtx.fillRect(x + 2, y + 2, bw - 4, bh - 4);
      }
    }

    const map = new THREE.CanvasTexture(colorCanvas);
    map.wrapS = THREE.RepeatWrapping;
    map.wrapT = THREE.RepeatWrapping;
    map.repeat.set(2, 2);
    map.colorSpace = THREE.SRGBColorSpace;

    const bump = new THREE.CanvasTexture(bumpCanvas);
    bump.wrapS = THREE.RepeatWrapping;
    bump.wrapT = THREE.RepeatWrapping;
    bump.repeat.set(2, 2);

    cached = { map, bump };
    textureCache.set(key, cached);
  }

  return new THREE.MeshStandardMaterial({
    map: cached.map,
    bumpMap: cached.bump,
    bumpScale: 0.03,
    roughness: 0.88,
    metalness: 0.05
  });
}

/**
 * 5. Вязаный шерстяной/войлочный материал (для свитера, шапки эльфа, бархатного дивана, кота)
 */
let sharedKnitBump: THREE.CanvasTexture | null = null;
function getSharedKnitBump(): THREE.CanvasTexture {
  if (!sharedKnitBump) {
    const size = 128;
    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = '#606060';
    ctx.fillRect(0, 0, size, size);

    for (let y = 0; y < size; y += 8) {
      for (let x = 0; x < size; x += 8) {
        ctx.fillStyle = '#dadada';
        ctx.beginPath();
        ctx.ellipse(x + 4, y + 4, 3, 2, 0.3, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    sharedKnitBump = new THREE.CanvasTexture(canvas);
    sharedKnitBump.wrapS = THREE.RepeatWrapping;
    sharedKnitBump.wrapT = THREE.RepeatWrapping;
    sharedKnitBump.repeat.set(6, 6);
  }
  return sharedKnitBump;
}

export function createTactileMaterial(
  color: number,
  options?: {
    emissive?: number;
    emissiveIntensity?: number;
    map?: THREE.Texture;
    roughness?: number;
    metalness?: number;
    knitBump?: boolean;
  }
): THREE.MeshPhysicalMaterial {
  return new THREE.MeshPhysicalMaterial({
    color,
    map: options?.map || null,
    bumpMap: options?.knitBump !== false ? getSharedKnitBump() : null,
    bumpScale: options?.knitBump !== false ? 0.012 : 0,
    roughness: options?.roughness ?? 0.78,
    metalness: options?.metalness ?? 0.04,
    sheen: 0.55,
    sheenRoughness: 0.75,
    sheenColor: new THREE.Color(color).lerp(new THREE.Color(0xffffff), 0.35),
    emissive: options?.emissive !== undefined ? new THREE.Color(options.emissive) : new THREE.Color(0x000000),
    emissiveIntensity: options?.emissiveIntensity || 0
  });
}
