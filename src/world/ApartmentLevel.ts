import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { PhysicsWorld } from '../physics/PhysicsWorld';
import { SoundEngine } from '../audio/SoundEngine';
import { GiftItem, GiftType } from '../entities/GiftItem';
import {
  createBrickMaterial,
  createCheckerTileMaterial,
  createChristmasRugMaterial,
  createTactileMaterial,
  createWoodParquetMaterial
} from '../rendering/TactileMaterials';


// ---------- Процедурные текстуры декора (обои, картины) ----------
function makeWallpaperTexture(base: string, stripe: string, dot: string): THREE.CanvasTexture {
  const size = 256;
  const c = document.createElement('canvas');
  c.width = size;
  c.height = size;
  const g = c.getContext('2d')!;
  g.fillStyle = base;
  g.fillRect(0, 0, size, size);
  g.fillStyle = stripe;
  for (let x = 0; x < size; x += 64) g.fillRect(x, 0, 32, size);
  // тонкие золотые линии на стыках полос
  g.fillStyle = dot;
  for (let x = 0; x < size; x += 64) {
    g.fillRect(x + 31, 0, 2, size);
    g.fillRect(x - 1, 0, 2, size);
  }
  // ромбики-орнамент
  for (let y = 16; y < size; y += 64) {
    for (let x = 16; x < size; x += 64) {
      g.beginPath();
      g.moveTo(x, y - 6);
      g.lineTo(x + 5, y);
      g.lineTo(x, y + 6);
      g.lineTo(x - 5, y);
      g.closePath();
      g.fill();
    }
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

function makePaintingTexture(kind: number): THREE.CanvasTexture {
  const w = 256;
  const h = 192;
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const g = c.getContext('2d')!;
  const sky = g.createLinearGradient(0, 0, 0, h);
  const palettes = [
    ['#1e3a8a', '#7dd3fc'],
    ['#7c2d12', '#fbbf24'],
    ['#312e81', '#f9a8d4'],
    ['#064e3b', '#a7f3d0']
  ];
  const pal = palettes[kind % palettes.length];
  sky.addColorStop(0, pal[0]);
  sky.addColorStop(1, pal[1]);
  g.fillStyle = sky;
  g.fillRect(0, 0, w, h);
  // луна / солнце
  g.fillStyle = 'rgba(255,248,220,0.95)';
  g.beginPath();
  g.arc(60 + kind * 35, 50, 20, 0, Math.PI * 2);
  g.fill();
  // холмы
  const hill = ['#f8fafc', '#e2e8f0', '#cbd5e1'];
  for (let i = 0; i < 3; i++) {
    g.fillStyle = hill[i];
    g.beginPath();
    g.moveTo(0, h);
    for (let x = 0; x <= w; x += 8) {
      g.lineTo(x, 120 + i * 22 + Math.sin(x * 0.03 + i * 2 + kind) * 14);
    }
    g.lineTo(w, h);
    g.closePath();
    g.fill();
  }
  // ёлочки
  g.fillStyle = '#14532d';
  for (let i = 0; i < 5; i++) {
    const tx = 30 + i * 48 + ((i * 17 + kind * 13) % 20);
    const ty = 130 + (i % 2) * 14;
    g.beginPath();
    g.moveTo(tx, ty - 34);
    g.lineTo(tx + 11, ty);
    g.lineTo(tx - 11, ty);
    g.closePath();
    g.fill();
  }
  // звёзды
  g.fillStyle = '#fff';
  for (let i = 0; i < 18; i++) g.fillRect((i * 53 + kind * 31) % w, (i * 29) % 80, 2, 2);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

export interface InteractiveObject {
  mesh: THREE.Mesh;
  body: RAPIER.RigidBody;
  isBroken?: boolean;
}

export class ApartmentLevel {
  public group: THREE.Group;
  public treePosition: THREE.Vector3;
  public fireplacePosition: THREE.Vector3;
  public bedroomPosition: THREE.Vector3;
  public bathroomPosition: THREE.Vector3;

  public interactiveObjects: InteractiveObject[] = [];
  public treeLights: THREE.PointLight[] = [];
  public garlandLights: THREE.PointLight[] = [];
  public garlandBulbs: THREE.Mesh[] = [];
  public gifts: GiftItem[] = [];
  public isSpookyHouse: boolean = false;

  private physics: PhysicsWorld;
  private soundEngine: SoundEngine;
  private scene: THREE.Scene;
  private colliders: RAPIER.Collider[] = [];
  private creakyZones: THREE.Box3[] = [];
  private carpetZones: THREE.Box3[] = [];
  private tileZones: THREE.Box3[] = [];
  private iceZones: THREE.Box3[] = [];
  private hidingZones: THREE.Box3[] = [];
  private exitRingMesh!: THREE.Mesh;
  private pendulumMesh!: THREE.Mesh;
  private fireFlames: THREE.Mesh[] = [];
  private fireLight!: THREE.PointLight;
  private clockTimer: number = 0;

  constructor(scene: THREE.Scene, physics: PhysicsWorld, soundEngine: SoundEngine) {
    this.scene = scene;
    this.physics = physics;
    this.soundEngine = soundEngine;
    this.group = new THREE.Group();

    this.fireplacePosition = new THREE.Vector3(-6.5, 0.0, 5.5);
    this.treePosition = new THREE.Vector3(5.5, 0, -5.5);
    this.bedroomPosition = new THREE.Vector3(-5.5, 0, -8.2);
    this.bathroomPosition = new THREE.Vector3(6.2, 0, 6.2);

    this.buildFiveRoomHouse();
    this.buildChristmasTree();
    this.buildFurnitureAndRooms();
    this.buildInteractiveObjects();
    this.buildDecor();
    this.spawnGifts(['SMALL', 'NOISY', 'HUGE']);

    this.group.traverse((child) => {
      if ((child as THREE.Mesh).isMesh) {
        child.castShadow = true;
        child.receiveShadow = true;
      }
    });

    scene.add(this.group);
  }

  private addStaticCollider(desc: RAPIER.ColliderDesc, body: RAPIER.RigidBody) {
    const col = this.physics.world.createCollider(desc, body);
    this.colliders.push(col);
    return col;
  }

  public configureForOrder(giftTypes: GiftType[], isSpooky: boolean, prepackedSackCount: number = 0) {
    this.isSpookyHouse = isSpooky;

    this.garlandLights.forEach((l) => {
      l.intensity = isSpooky ? 0.65 : 2.8;
      l.color.setHex(isSpooky ? 0x818cf8 : 0xffb347);
    });
    this.garlandBulbs.forEach((b) => {
      b.visible = !isSpooky;
    });

    this.spawnGifts(giftTypes, prepackedSackCount);
  }

  public spawnGifts(giftTypes: GiftType[] = ['SMALL', 'NOISY', 'HUGE'], prepackedSackCount: number = 0) {
    for (const g of this.gifts) {
      this.scene.remove(g.mesh);
      try {
        this.physics.world.removeRigidBody(g.body);
      } catch {}
    }
    this.gifts = [];

    let packedRemaining = prepackedSackCount;

    giftTypes.forEach((type, idx) => {
      const spawnPos = new THREE.Vector3(
        this.fireplacePosition.x + 2.2 + (idx % 2) * 1.1,
        0.55,
        this.fireplacePosition.z - 0.8 - idx * 1.1
      );
      const gift = new GiftItem(
        `apt-gift-${idx}-${Date.now()}`,
        type,
        spawnPos,
        this.scene,
        this.physics,
        this.soundEngine
      );

      if (packedRemaining > 0 && gift.canFitInSack()) {
        gift.isInSack = true;
        gift.setVisible(false);
        packedRemaining--;
      } else {
        gift.setVisible(this.group.visible);
      }

      this.gifts.push(gift);
    });
  }

  private buildFiveRoomHouse() {
    const R = this.physics.getRapier();

    // 1. Настоящий лакированный паркет с текстурой досок и рельефом фасок (как на референсе №3)
    const floorGeo = new THREE.BoxGeometry(24, 0.4, 22);
    const woodFloorMat = createWoodParquetMaterial(0x7c4319);
    const floorMesh = new THREE.Mesh(floorGeo, woodFloorMat);
    floorMesh.position.set(0, -0.2, 0);
    floorMesh.receiveShadow = true;
    this.group.add(floorMesh);

    const floorBody = this.physics.world.createRigidBody(R.RigidBodyDesc.fixed().setTranslation(0, -0.2, 0));
    this.addStaticCollider(R.ColliderDesc.cuboid(12, 0.2, 11), floorBody);

    // 2. Стены дома с деревянными стеновыми панелями (Wainscoting) внизу и теплыми обоями сверху
    // Тёплые обои в полоску: у каждой стены свой оттенок
    const wallpaperSets = [
      ['#6b2433', '#7e2f40', '#e8b86a'], // задняя — винный бордо
      ['#2c5a45', '#386b54', '#e8b86a'], // передняя — еловый зелёный
      ['#8a5a2e', '#9b6a3a', '#f3d9a4'], // левая — тёплая охра
      ['#334a73', '#3f5a88', '#e8b86a'], // правая — вечерний синий
      ['#d9c7a0', '#e4d5b2', '#b45309'], // перегородки — кремовые
      ['#d9c7a0', '#e4d5b2', '#b45309']
    ];
    const baseboardMat = createWoodParquetMaterial(0x2a1408);
    const crownMat = createTactileMaterial(0xf5e6c8, { roughness: 0.6, knitBump: false });
    const wainscotMat = createWoodParquetMaterial(0x4a2511);
    const trimGoldMat = new THREE.MeshStandardMaterial({ color: 0xb45309, roughness: 0.4, metalness: 0.3 });

    const wallsData = [
      { size: [24, 4.4, 0.4], pos: [0, 2.2, -11] },   // Задняя стена
      { size: [24, 4.4, 0.4], pos: [0, 2.2, 11] },    // Передняя стена
      { size: [0.4, 4.4, 22], pos: [-12, 2.2, 0] },   // Левая стена
      { size: [0.4, 4.4, 22], pos: [12, 2.2, 0] },    // Правая стена
      { size: [7.5, 4.4, 0.32], pos: [-6.5, 2.2, -3.8] }, // Перегородка Кабинета Деда
      { size: [0.32, 4.4, 7.5], pos: [2.2, 2.2, 5.2] }    // Перегородка Кухни
    ];

    wallsData.forEach((w, wallIdx) => {
      const ws = wallpaperSets[wallIdx];
      const wpTex = makeWallpaperTexture(ws[0], ws[1], ws[2]);
      wpTex.repeat.set(Math.max(w.size[0], w.size[2]) / 3, 1.6);
      const upperWallMat = createTactileMaterial(0xffffff, { map: wpTex, roughness: 0.88, knitBump: false });
      const wallGroup = new THREE.Group();
      wallGroup.position.set(w.pos[0], 0, w.pos[2]);

      // Основная стена
      const upperMesh = new THREE.Mesh(new THREE.BoxGeometry(w.size[0], w.size[1], w.size[2]), upperWallMat);
      upperMesh.position.y = w.size[1] / 2;
      wallGroup.add(upperMesh);

      // Деревянная панель (Wainscoting) по низу стены (высота 1.15м)
      const panelMesh = new THREE.Mesh(
        new THREE.BoxGeometry(w.size[0] + 0.04, 1.15, w.size[2] + 0.04),
        wainscotMat
      );
      panelMesh.position.y = 0.575;
      wallGroup.add(panelMesh);

      // Карниз-молдинг над панелью
      const railMesh = new THREE.Mesh(
        new THREE.BoxGeometry(w.size[0] + 0.08, 0.08, w.size[2] + 0.08),
        trimGoldMat
      );
      railMesh.position.y = 1.16;
      wallGroup.add(railMesh);

      // Плинтус и потолочный карниз
      const baseboard = new THREE.Mesh(
        new THREE.BoxGeometry(w.size[0] + 0.07, 0.22, w.size[2] + 0.07),
        baseboardMat
      );
      baseboard.position.y = 0.11;
      wallGroup.add(baseboard);
      const crown = new THREE.Mesh(
        new THREE.BoxGeometry(w.size[0] + 0.1, 0.18, w.size[2] + 0.1),
        crownMat
      );
      crown.position.y = w.size[1] - 0.09;
      wallGroup.add(crown);

      this.group.add(wallGroup);

      const wallBody = this.physics.world.createRigidBody(
        R.RigidBodyDesc.fixed().setTranslation(w.pos[0], w.pos[1], w.pos[2])
      );
      this.addStaticCollider(
        R.ColliderDesc.cuboid(w.size[0] / 2, w.size[1] / 2, w.size[2] / 2),
        wallBody
      );
    });

    // Настенные теплые светильники-бра (Wall Sconces) с мягким янтарным светом (как на референсах)
    const sconcePositions = [
      [-11.6, 2.5, 1.5],
      [-11.6, 2.5, -6.5],
      [-2.5, 2.5, -10.6],
      [7.5, 2.5, -10.6],
      [11.6, 2.5, 3.5]
    ];
    const brassMat = new THREE.MeshStandardMaterial({ color: 0xd97706, metalness: 0.85, roughness: 0.25 });
    const shadeMat = new THREE.MeshStandardMaterial({
      color: 0xffedd5,
      emissive: 0xff9900,
      emissiveIntensity: 2.2
    });

    sconcePositions.forEach((sp, sconceIdx) => {
      const sGroup = new THREE.Group();
      sGroup.position.set(sp[0], sp[1], sp[2]);

      const mount = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, 0.06, 12), brassMat);
      mount.rotation.x = Math.PI / 2;
      sGroup.add(mount);

      const shade = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.09, 0.24, 14), shadeMat);
      shade.position.y = 0.14;
      sGroup.add(shade);

      // Реальный свет только у каждого второго бра (остальные светятся эмиссивным плафоном) — экономим GPU
      if (sconceIdx % 2 === 0) {
        const light = new THREE.PointLight(0xffa733, 4.2, 14, 1.5);
        light.position.set(0, 0.2, 0);
        sGroup.add(light);
        this.garlandLights.push(light);
      }

      this.group.add(sGroup);
    });

    // --- ЗОНА 1: ВЯЗАНЫЙ РОЖДЕСТВЕНСКИЙ КОВЁР С УЗОРОМ В ГОСТИНОЙ (референс №3) ---
    const carpetMat = createChristmasRugMaterial();
    const carpetMesh = new THREE.Mesh(new THREE.BoxGeometry(7.6, 0.05, 6.4), carpetMat);
    carpetMesh.position.set(4.5, 0.025, -3.5);
    carpetMesh.receiveShadow = true;
    this.group.add(carpetMesh);
    this.carpetZones.push(new THREE.Box3(new THREE.Vector3(0.7, 0, -6.8), new THREE.Vector3(8.3, 1.5, -0.2)));

    // --- ЗОНА 2: СКРИПУЧИЕ ДОСКИ ПРИХОЖЕЙ ---
    const creakMat = createWoodParquetMaterial(0x5c2d0c);
    const creakAreas = [
      { pos: [-0.5, 0.02, 2.2], size: [2.6, 0.03, 3.2] },
      { pos: [-2.8, 0.02, -2.2], size: [2.2, 0.03, 2.4] }
    ];
    creakAreas.forEach((c) => {
      const m = new THREE.Mesh(new THREE.BoxGeometry(c.size[0], c.size[1], c.size[2]), creakMat);
      m.position.set(c.pos[0], c.pos[1], c.pos[2]);
      this.group.add(m);
      this.creakyZones.push(
        new THREE.Box3(
          new THREE.Vector3(c.pos[0] - c.size[0] / 2, 0, c.pos[2] - c.size[2] / 2),
          new THREE.Vector3(c.pos[0] + c.size[0] / 2, 1.2, c.pos[2] + c.size[2] / 2)
        )
      );
    });

    // --- ЗОНА 3: ГЛЯНЦЕВАЯ ШАХМАТНАЯ КАФЕЛЬНАЯ ПЛИТКА НА КУХНЕ (референс №2) ---
    const kitchenTileMat = createCheckerTileMaterial();
    const kitchenTileMesh = new THREE.Mesh(new THREE.BoxGeometry(9.0, 0.035, 8.8), kitchenTileMat);
    kitchenTileMesh.position.set(7.0, 0.018, 5.8);
    kitchenTileMesh.receiveShadow = true;
    this.group.add(kitchenTileMesh);
    this.tileZones.push(new THREE.Box3(new THREE.Vector3(2.5, 0, 1.4), new THREE.Vector3(11.5, 1.5, 10.2)));

    // --- ЗОНА 5: ЗАМЁРЗШАЯ ЛУЖА ЛЬДА В ГАРАЖЕ (референс №4) ---
    const icePuddle = new THREE.Mesh(
      new THREE.CylinderGeometry(2.2, 2.4, 0.03, 24),
      new THREE.MeshPhysicalMaterial({
        color: 0xbae6fd,
        roughness: 0.04,
        metalness: 0.15,
        transmission: 0.4,
        transparent: true,
        opacity: 0.85,
        clearcoat: 1.0
      })
    );
    icePuddle.position.set(-8.5, 0.02, 7.8);
    this.group.add(icePuddle);
    this.iceZones.push(new THREE.Box3(new THREE.Vector3(-10.8, 0, 5.5), new THREE.Vector3(-6.2, 1.5, 10.1)));

    // --- ТЁПЛЫЕ ПРАЗДНИЧНЫЕ ГИРЛЯНДЫ ПОД ПОТОЛКОМ (как на референсе №4) ---
    const warmGoldColors = [0xffb703, 0xfb8500, 0xffe5ec, 0xffd166];
    const garlandMats = warmGoldColors.map(
      (c) => new THREE.MeshStandardMaterial({ color: c, emissive: c, emissiveIntensity: 3.0 })
    );
    const bulbGeo = new THREE.SphereGeometry(0.065, 10, 10);
    for (let i = 0; i < 28; i++) {
      const gx = -10 + (i % 14) * 1.5;
      const gz = i < 14 ? -10.5 : 0.0;
      const sag = Math.sin(((i % 14) / 13) * Math.PI) * 0.35;
      const col = warmGoldColors[i % warmGoldColors.length];
      const bulb = new THREE.Mesh(bulbGeo, garlandMats[i % garlandMats.length]);
      void col;
      bulb.position.set(gx, 3.85 - sag, gz);
      this.group.add(bulb);
      this.garlandBulbs.push(bulb);
    }

    this.buildFireplace();
    this.buildWindow();
  }


  /** Декор без коллайдеров: картины, венки, рамы, ковры, торшеры (эмиссивные, без лишних PointLight) */
  private buildDecor() {
    const goldFrame = new THREE.MeshStandardMaterial({ color: 0xb8862b, metalness: 0.8, roughness: 0.35 });
    const darkFrame = createWoodParquetMaterial(0x2a1408);

    // --- Картины на стенах (внутренняя сторона стен, лицом в комнаты) ---
    // [x, y, z, rotY, w, h, kind]
    const frames: number[][] = [
      [-9, 2.5, -10.56, 0, 1.5, 1.1, 0],
      [-5.2, 2.6, -10.56, 0, 0.9, 1.2, 1],
      [-11.56, 2.5, -9, Math.PI / 2, 1.3, 1.0, 2],
      [-11.56, 2.5, -1.5, Math.PI / 2, 1.5, 1.1, 3],
      [-11.56, 2.5, 9, Math.PI / 2, 1.2, 0.9, 1],
      [-9, 2.5, 10.56, Math.PI, 1.5, 1.1, 2],
      [-1.5, 2.5, 10.56, Math.PI, 1.3, 1.0, 0],
      [1, 2.5, -10.56, 0, 0.8, 1.0, 3],
      [11.56, 2.5, -3, -Math.PI / 2, 1.5, 1.1, 1],
      [11.56, 2.5, -7.5, -Math.PI / 2, 1.2, 0.9, 0],
      [11.56, 2.5, 9, -Math.PI / 2, 1.0, 1.0, 3],
      [-6.5, 2.4, -3.6, 0, 1.4, 1.0, 2]
    ];
    const paintTex = [0, 1, 2, 3].map((k) => makePaintingTexture(k));
    frames.forEach((f, i) => {
      const [x, y, z, ry, w, h, kind] = f;
      const g = new THREE.Group();
      g.position.set(x, y, z);
      g.rotation.y = ry;
      const fm = i % 2 === 0 ? goldFrame : darkFrame;
      const border = 0.09;
      const outer = new THREE.Mesh(new THREE.BoxGeometry(w + border * 2, h + border * 2, 0.06), fm);
      g.add(outer);
      const art = new THREE.Mesh(
        new THREE.PlaneGeometry(w, h),
        new THREE.MeshStandardMaterial({
          map: paintTex[kind],
          emissiveMap: paintTex[kind],
          emissive: 0xffffff,
          emissiveIntensity: 0.22,
          roughness: 0.7
        })
      );
      art.position.z = 0.035;
      g.add(art);
      this.group.add(g);
    });

    // --- Рождественские венки с бантом ---
    const wreathMat = createTactileMaterial(0x166534, { roughness: 0.85 });
    const bowMat = createTactileMaterial(0xdc2626, { roughness: 0.6 });
    const berryMat = new THREE.MeshStandardMaterial({ color: 0xef4444, emissive: 0xb91c1c, emissiveIntensity: 0.6 });
    const wreaths: number[][] = [
      [-2.6, 3.0, 10.56, Math.PI],
      [4.4, 3.0, -10.56, 0],
      [-11.56, 3.0, 5.2, Math.PI / 2],
      [2.34, 3.0, 5.2, Math.PI / 2],
      [11.56, 3.0, -5, -Math.PI / 2]
    ];
    wreaths.forEach((wr) => {
      const g = new THREE.Group();
      g.position.set(wr[0], wr[1], wr[2]);
      g.rotation.y = wr[3];
      const ring = new THREE.Mesh(new THREE.TorusGeometry(0.34, 0.1, 10, 24), wreathMat);
      g.add(ring);
      for (let k = 0; k < 6; k++) {
        const a = k * 1.05;
        const berry = new THREE.Mesh(new THREE.SphereGeometry(0.035, 6, 6), berryMat);
        berry.position.set(Math.cos(a) * 0.34, Math.sin(a) * 0.34, 0.08);
        g.add(berry);
      }
      const bowL = new THREE.Mesh(new THREE.ConeGeometry(0.1, 0.2, 6), bowMat);
      bowL.rotation.z = Math.PI / 2 + 0.4;
      bowL.position.set(-0.1, -0.33, 0.08);
      const bowR = bowL.clone();
      bowR.rotation.z = -Math.PI / 2 - 0.4;
      bowR.position.x = 0.1;
      g.add(bowL, bowR);
      this.group.add(g);
    });

    // --- Ковры (чисто визуальные) ---
    const roundRug = (x: number, z: number, r: number, color: number, ringColor: number) => {
      const rug = new THREE.Mesh(
        new THREE.CylinderGeometry(r, r, 0.03, 36),
        createTactileMaterial(color, { roughness: 0.95 })
      );
      rug.position.set(x, 0.017, z);
      const ring = new THREE.Mesh(
        new THREE.TorusGeometry(r * 0.78, 0.035, 6, 40),
        createTactileMaterial(ringColor, { roughness: 0.9 })
      );
      ring.rotation.x = Math.PI / 2;
      ring.position.y = 0.025;
      rug.add(ring);
      this.group.add(rug);
    };
    roundRug(-7.2, -5.9, 1.25, 0x7f1d1d, 0xfbbf24); // спальня
    roundRug(-7.4, -1.1, 1.0, 0x1e3a5f, 0xe2e8f0); // кабинет

    // Тёмный дубовый настил в спальне/кабинете для разнообразия пола
    const studyFloor = new THREE.Mesh(
      new THREE.BoxGeometry(9.4, 0.012, 6.8),
      createWoodParquetMaterial(0x4a2511)
    );
    studyFloor.position.set(-7.05, 0.006, -7.2);
    studyFloor.receiveShadow = true;
    this.group.add(studyFloor);

    // --- Торшеры с эмиссивным абажуром (в углах, у стен) ---
    const lampPoleMat = new THREE.MeshStandardMaterial({ color: 0x3f2a14, metalness: 0.6, roughness: 0.4 });
    const lampShadeMat = new THREE.MeshStandardMaterial({
      color: 0xfde7b0,
      emissive: 0xffa94d,
      emissiveIntensity: 1.8,
      side: THREE.DoubleSide
    });
    [
      [-11.2, -10.2],
      [11.2, -10.2],
      [-11.2, 10.2],
      [1.7, 10.2]
    ].forEach(([lx, lz]) => {
      const lamp = new THREE.Group();
      lamp.position.set(lx, 0, lz);
      const base = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.26, 0.06, 14), lampPoleMat);
      base.position.y = 0.03;
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 1.5, 8), lampPoleMat);
      pole.position.y = 0.78;
      const shade = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.32, 0.45, 16, 1, true), lampShadeMat);
      shade.position.y = 1.7;
      lamp.add(base, pole, shade);
      this.group.add(lamp);
    });

    // Тёплое свечение камина на полу (аддитивное пятно вместо второго света)
    const hearthGlow = new THREE.Mesh(
      new THREE.CircleGeometry(2.6, 32),
      new THREE.MeshBasicMaterial({
        color: 0xff7a1a,
        transparent: true,
        opacity: 0.14,
        depthWrite: false,
        blending: THREE.AdditiveBlending
      })
    );
    hearthGlow.rotation.x = -Math.PI / 2;
    hearthGlow.position.set(this.fireplacePosition.x, 0.045, this.fireplacePosition.z + 1.9);
    this.group.add(hearthGlow);
  }

  private buildFireplace() {
    const R = this.physics.getRapier();
    const brickMat = createBrickMaterial(0x9a3412);
    const woodMantleMat = createWoodParquetMaterial(0x451a03);
    const sootMat = new THREE.MeshStandardMaterial({ color: 0x111827, roughness: 0.95 });

    const fireplaceGroup = new THREE.Group();
    fireplaceGroup.position.copy(this.fireplacePosition);

    // Высокая кирпичная труба камина до потолка (как на референсе №3 и №4)
    const chimneyBreast = new THREE.Mesh(new THREE.BoxGeometry(3.0, 4.4, 1.3), brickMat);
    chimneyBreast.position.set(0, 2.2, -0.35);
    fireplaceGroup.add(chimneyBreast);

    // Левая и правая кирпичные колонны топки
    const leftCol = new THREE.Mesh(new THREE.BoxGeometry(0.75, 2.1, 1.4), brickMat);
    leftCol.position.set(-1.12, 1.05, 0.1);
    fireplaceGroup.add(leftCol);

    const rightCol = new THREE.Mesh(new THREE.BoxGeometry(0.75, 2.1, 1.4), brickMat);
    rightCol.position.set(1.12, 1.05, 0.1);
    fireplaceGroup.add(rightCol);

    // Каминная полка из массива дуба
    const mantle = new THREE.Mesh(new THREE.BoxGeometry(3.4, 0.24, 1.65), woodMantleMat);
    mantle.position.set(0, 2.15, 0.15);
    fireplaceGroup.add(mantle);

    // Кирпичный порожек перед камином
    const hearthStep = new THREE.Mesh(new THREE.BoxGeometry(3.4, 0.16, 1.9), brickMat);
    hearthStep.position.set(0, 0.08, 0.35);
    fireplaceGroup.add(hearthStep);

    // Внутренняя ниша очага
    const hearthInside = new THREE.Mesh(new THREE.BoxGeometry(1.5, 1.75, 0.9), sootMat);
    hearthInside.position.set(0, 0.95, -0.1);
    fireplaceGroup.add(hearthInside);

    // Поленья и живое пламя внутри камина
    const logMat = new THREE.MeshStandardMaterial({ color: 0x3b1908, roughness: 0.9 });
    [-0.2, 0.2].forEach((lx, i) => {
      const log = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.1, 0.85, 8), logMat);
      log.rotation.z = Math.PI / 2 + (i === 0 ? 0.15 : -0.15);
      log.position.set(0, 0.24, 0.1 + lx * 0.4);
      fireplaceGroup.add(log);
    });

    const flameColors = [0xff4500, 0xff9e00, 0xffd000];
    for (let f = 0; f < 5; f++) {
      const flameMat = new THREE.MeshStandardMaterial({
        color: flameColors[f % flameColors.length],
        emissive: flameColors[f % flameColors.length],
        emissiveIntensity: 3.2
      });
      const flame = new THREE.Mesh(new THREE.ConeGeometry(0.18 - f * 0.02, 0.65, 8), flameMat);
      flame.position.set(-0.36 + f * 0.18, 0.48, 0.12);
      fireplaceGroup.add(flame);
      this.fireFlames.push(flame);
    }

    // Рождественские вязаные носки для подарков на каминной полке (как на референсе №3!)
    const redKnit = createTactileMaterial(0xb91c1c);
    const greenKnit = createTactileMaterial(0x15803d);
    const whiteKnit = createTactileMaterial(0xf8fafc);

    [-1.0, 0.0, 1.0].forEach((sx, idx) => {
      const stocking = new THREE.Group();
      stocking.position.set(sx, 1.92, 0.96);

      const cuff = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.11, 0.14, 10), whiteKnit);
      stocking.add(cuff);

      const leg = new THREE.Mesh(
        new THREE.CylinderGeometry(0.1, 0.095, 0.34, 10),
        idx % 2 === 0 ? redKnit : greenKnit
      );
      leg.position.y = -0.22;
      stocking.add(leg);

      const foot = new THREE.Mesh(
        new THREE.SphereGeometry(0.11, 10, 10),
        idx % 2 === 0 ? greenKnit : redKnit
      );
      foot.scale.set(1.3, 0.85, 0.9);
      foot.position.set(0.08, -0.36, 0);
      stocking.add(foot);

      fireplaceGroup.add(stocking);
    });

    // Светящееся кольцо возврата на Базу Санты перед камином
    const exitRingGeo = new THREE.RingGeometry(0.85, 1.12, 32);
    const exitRingMat = new THREE.MeshBasicMaterial({
      color: 0x38bdf8,
      side: THREE.DoubleSide,
      transparent: true,
      opacity: 0.75
    });
    this.exitRingMesh = new THREE.Mesh(exitRingGeo, exitRingMat);
    this.exitRingMesh.rotation.x = -Math.PI / 2;
    this.exitRingMesh.position.set(1.8, 0.03, 0);
    fireplaceGroup.add(this.exitRingMesh);

    this.fireLight = new THREE.PointLight(0xff6b1a, 5.5, 14, 1.4);
    this.fireLight.position.set(0, 0.8, 1.1);
    this.fireLight.castShadow = false;
    fireplaceGroup.add(this.fireLight);

    this.group.add(fireplaceGroup);

    const body = this.physics.world.createRigidBody(
      R.RigidBodyDesc.fixed().setTranslation(this.fireplacePosition.x, 1.1, this.fireplacePosition.z)
    );
    this.addStaticCollider(R.ColliderDesc.cuboid(1.5, 1.1, 0.75), body);
  }

  private buildChristmasTree() {
    const treeGroup = new THREE.Group();
    treeGroup.position.copy(this.treePosition);

    const trunkMat = createWoodParquetMaterial(0x451a03);
    const trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.24, 0.34, 0.95, 12), trunkMat);
    trunk.position.y = 0.45;
    treeGroup.add(trunk);

    // Пышная многоярусная хвоя (как на референсе №3)
    const needleMat = createTactileMaterial(0x14532d, { roughness: 0.82 });
    const tiers = [
      { rad: 2.05, h: 1.45, y: 1.15 },
      { rad: 1.72, h: 1.35, y: 1.95 },
      { rad: 1.36, h: 1.25, y: 2.7 },
      { rad: 0.98, h: 1.1, y: 3.35 },
      { rad: 0.58, h: 0.9, y: 3.9 }
    ];

    tiers.forEach((t) => {
      const cone = new THREE.Mesh(new THREE.ConeGeometry(t.rad, t.h, 16), needleMat);
      cone.position.y = t.y;
      treeGroup.add(cone);
    });

    // Сияющая звезда на верхушке
    const starMat = new THREE.MeshStandardMaterial({
      color: 0xfbbf24,
      emissive: 0xffaa00,
      emissiveIntensity: 4.0,
      metalness: 0.8,
      roughness: 0.2
    });
    const star = new THREE.Mesh(new THREE.OctahedronGeometry(0.28), starMat);
    star.position.y = 4.45;
    treeGroup.add(star);

    // 55 теплых золотых огоньков гирлянды + красные и золотые ёлочные шары (как на референсе №3!)
    const warmBulbMat = new THREE.MeshStandardMaterial({
      color: 0xfff3b0,
      emissive: 0xffaa00,
      emissiveIntensity: 3.5
    });
    const redBaubleMat = new THREE.MeshPhysicalMaterial({
      color: 0xdc2626,
      roughness: 0.15,
      metalness: 0.7,
      clearcoat: 1.0
    });
    const goldBaubleMat = new THREE.MeshPhysicalMaterial({
      color: 0xf59e0b,
      roughness: 0.18,
      metalness: 0.85,
      clearcoat: 1.0
    });

    // Гирлянда и шары — InstancedMesh (мало draw call-ов), эмиссивные огоньки светятся через bloom
    const bulbCount = 70;
    const bulbColors = [0xffd166, 0xff6b6b, 0x7dd3fc, 0xa7f3d0, 0xfff3b0];
    const bulbInst = new THREE.InstancedMesh(
      new THREE.SphereGeometry(0.055, 8, 8),
      new THREE.MeshBasicMaterial({ color: 0xffffff, toneMapped: false }),
      bulbCount
    );
    const baubleGeo = new THREE.SphereGeometry(0.115, 14, 14);
    const baubleCount = 26;
    const baubleInst = new THREE.InstancedMesh(baubleGeo, redBaubleMat, baubleCount);
    const goldInst = new THREE.InstancedMesh(baubleGeo, goldBaubleMat, baubleCount);
    const dummy = new THREE.Object3D();
    const tmpCol = new THREE.Color();
    let redN = 0;
    let goldN = 0;

    for (let i = 0; i < bulbCount; i++) {
      const t = i / bulbCount;
      const angle = i * 0.85;
      const height = 0.75 + t * 3.35;
      const radius = (1.92 - t * 1.45) * 0.96;
      dummy.position.set(Math.cos(angle) * radius, height, Math.sin(angle) * radius);
      dummy.scale.setScalar(1);
      dummy.updateMatrix();
      bulbInst.setMatrixAt(i, dummy.matrix);
      bulbInst.setColorAt(i, tmpCol.setHex(bulbColors[i % bulbColors.length]).multiplyScalar(2.2));

      if (i % 2 === 0) {
        dummy.position.set(
          Math.cos(angle + 0.45) * radius * 1.0,
          height - 0.1,
          Math.sin(angle + 0.45) * radius * 1.0
        );
        dummy.updateMatrix();
        if (i % 4 === 0 && redN < baubleCount) baubleInst.setMatrixAt(redN++, dummy.matrix);
        else if (goldN < baubleCount) goldInst.setMatrixAt(goldN++, dummy.matrix);
      }
    }
    baubleInst.count = redN;
    goldInst.count = goldN;
    treeGroup.add(bulbInst, baubleInst, goldInst);

    // Серебристая мишура спиралью
    const tinselMat = new THREE.MeshStandardMaterial({
      color: 0xe2e8f0,
      emissive: 0xfff1c9,
      emissiveIntensity: 0.35,
      metalness: 0.9,
      roughness: 0.3
    });
    const tinselPts: THREE.Vector3[] = [];
    for (let k = 0; k <= 90; k++) {
      const t = k / 90;
      const ang = t * Math.PI * 2 * 5.5;
      const r = (1.95 - t * 1.5) * 0.93;
      tinselPts.push(new THREE.Vector3(Math.cos(ang) * r, 0.8 + t * 3.2, Math.sin(ang) * r));
    }
    const tinsel = new THREE.Mesh(
      new THREE.TubeGeometry(new THREE.CatmullRomCurve3(tinselPts), 180, 0.025, 5, false),
      tinselMat
    );
    treeGroup.add(tinsel);

    // Тканевая юбка под ёлкой
    const skirt = new THREE.Mesh(
      new THREE.CylinderGeometry(1.25, 1.35, 0.07, 28),
      createTactileMaterial(0xb91c1c, { roughness: 0.9 })
    );
    skirt.position.y = 0.085;
    treeGroup.add(skirt);

    const treeWarmLight = new THREE.PointLight(0xffae33, 5.0, 12, 1.4);
    treeWarmLight.position.set(0, 2.4, 0.8);
    treeGroup.add(treeWarmLight);
    this.treeLights.push(treeWarmLight);

    // Зона выгрузки подарков под Ёлкой
    const ringGeo = new THREE.RingGeometry(1.15, 1.48, 32);
    const ringMat = new THREE.MeshBasicMaterial({
      color: 0xfbbf24,
      side: THREE.DoubleSide,
      transparent: true,
      opacity: 0.75
    });
    const dropZone = new THREE.Mesh(ringGeo, ringMat);
    dropZone.rotation.x = -Math.PI / 2;
    dropZone.position.set(0.5, 0.075, 1.4);
    treeGroup.add(dropZone);

    this.group.add(treeGroup);

    const R = this.physics.getRapier();
    const treeBody = this.physics.world.createRigidBody(
      R.RigidBodyDesc.fixed().setTranslation(this.treePosition.x, 2, this.treePosition.z)
    );
    this.addStaticCollider(R.ColliderDesc.cylinder(2, 0.85), treeBody);
  }

  private buildFurnitureAndRooms() {
    const R = this.physics.getRapier();

    // 1. ПУХЛЫЙ ЗЕЛЕНЫЙ БАРХАТНЫЙ ДИВАН С ПОДУШКАМИ (точь-в-точь как на референсе Гостиной №3!)
    const sofaGroup = new THREE.Group();
    sofaGroup.position.set(2.8, 0, 0.5);

    const velvetGreenMat = createTactileMaterial(0x1f4e29, { roughness: 0.72 });
    const legFeltMat = createTactileMaterial(0x574c43, { roughness: 0.85 });

    // 4 массивные войлочно-деревянные ножки (высота 0.34м — под диван можно залезть!)
    [
      [-1.65, 0.17, -0.68],
      [1.65, 0.17, -0.68],
      [-1.65, 0.17, 0.68],
      [1.65, 0.17, 0.68]
    ].forEach((lp) => {
      const leg = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.085, 0.34, 12), legFeltMat);
      leg.position.set(lp[0], lp[1], lp[2]);
      sofaGroup.add(leg);
    });

    // Основание дивана
    const baseFrame = new THREE.Mesh(new THREE.BoxGeometry(3.7, 0.22, 1.65), velvetGreenMat);
    baseFrame.position.set(0, 0.42, 0);
    sofaGroup.add(baseFrame);

    // 3 пухлые подушки сиденья со скругленными краями
    [-1.12, 0, 1.12].forEach((cx) => {
      const cushion = new THREE.Mesh(new THREE.BoxGeometry(1.08, 0.24, 1.45), velvetGreenMat);
      cushion.position.set(cx, 0.62, -0.05);
      sofaGroup.add(cushion);
    });

    // Пышная спинка и пухлые подлокотники
    const backRest = new THREE.Mesh(new THREE.BoxGeometry(3.6, 0.85, 0.45), velvetGreenMat);
    backRest.position.set(0, 1.05, 0.62);
    sofaGroup.add(backRest);

    [-1.75, 1.75].forEach((ax) => {
      const armRest = new THREE.Mesh(new THREE.CylinderGeometry(0.24, 0.24, 1.65, 14), velvetGreenMat);
      armRest.rotation.x = Math.PI / 2;
      armRest.position.set(ax, 0.82, 0);
      sofaGroup.add(armRest);
    });

    this.group.add(sofaGroup);
    const sofaBody = this.physics.world.createRigidBody(R.RigidBodyDesc.fixed().setTranslation(2.8, 0.55, 0.5));
    this.addStaticCollider(R.ColliderDesc.cuboid(1.85, 0.22, 0.82), sofaBody);
    this.hidingZones.push(new THREE.Box3(new THREE.Vector3(0.9, 0, -0.35), new THREE.Vector3(4.7, 0.55, 1.35)));

    // 2. ПРИХОЖАЯ: Деревянная Лестница с балясинами и Старинные Часы с маятником (референсы №1 и №4)
    const woodMat = createWoodParquetMaterial(0x5c2d0c);
    const goldMat = new THREE.MeshStandardMaterial({ color: 0xfacc15, metalness: 0.85, roughness: 0.2 });

    const stairsGroup = new THREE.Group();
    stairsGroup.position.set(-0.6, 0, -6.5);
    for (let s = 0; s < 8; s++) {
      const step = new THREE.Mesh(new THREE.BoxGeometry(2.2, 0.22, 0.45), woodMat);
      step.position.set(0, 0.11 + s * 0.24, s * -0.42);
      stairsGroup.add(step);

      // Балясины перил
      const baluster = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 0.75, 8), woodMat);
      baluster.position.set(1.0, 0.55 + s * 0.24, s * -0.42);
      stairsGroup.add(baluster);
    }
    this.group.add(stairsGroup);

    // Старинные напольные часы
    const clockGroup = new THREE.Group();
    clockGroup.position.set(-2.2, 0, -3.3);
    const clockCase = new THREE.Mesh(new THREE.BoxGeometry(0.76, 2.6, 0.54), woodMat);
    clockCase.position.y = 1.3;
    clockGroup.add(clockCase);

    const clockFace = new THREE.Mesh(
      new THREE.CylinderGeometry(0.26, 0.26, 0.06, 20),
      new THREE.MeshStandardMaterial({ color: 0xfef3c7, emissive: 0xfde68a, emissiveIntensity: 0.7 })
    );
    clockFace.rotation.x = Math.PI / 2;
    clockFace.position.set(0, 2.05, 0.26);
    clockGroup.add(clockFace);

    this.pendulumMesh = new THREE.Mesh(new THREE.SphereGeometry(0.11, 12, 12), goldMat);
    this.pendulumMesh.position.set(0, 1.15, 0.28);
    clockGroup.add(this.pendulumMesh);
    this.group.add(clockGroup);

    // 3. КУХНЯ (референс №2): Массивный деревянный стол со свисающей скатертью, стопки тарелок, табуретка и светящийся холодильник!
    const kitchenTable = new THREE.Group();
    kitchenTable.position.set(6.8, 0, 5.5);

    const tableTop = new THREE.Mesh(new THREE.BoxGeometry(3.3, 0.16, 1.9), woodMat);
    tableTop.position.y = 1.08;
    kitchenTable.add(tableTop);

    [
      [-1.4, 0.54, -0.78],
      [1.4, 0.54, -0.78],
      [-1.4, 0.54, 0.78],
      [1.4, 0.54, 0.78]
    ].forEach((lp) => {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.16, 1.08, 0.16), woodMat);
      leg.position.set(lp[0], lp[1], lp[2]);
      kitchenTable.add(leg);
    });

    // Свисающая тканевая скатерть сбоку стола (как на референсе Кухни №2!)
    const clothMat = createTactileMaterial(0xd6d3d1, { roughness: 0.92 });
    const clothDrape = new THREE.Mesh(new THREE.BoxGeometry(0.95, 1.06, 1.82), clothMat);
    clothDrape.position.set(-1.35, 0.56, 0);
    clothDrape.rotation.z = 0.12;
    kitchenTable.add(clothDrape);

    // Высокие стопки керамических тарелок на краю стола (как на референсе №2!)
    const plateMat = new THREE.MeshPhysicalMaterial({ color: 0xf8fafc, roughness: 0.2, clearcoat: 0.8 });
    for (let p = 0; p < 8; p++) {
      const plate = new THREE.Mesh(new THREE.CylinderGeometry(0.24, 0.18, 0.045, 16), plateMat);
      plate.position.set(0.95 + Math.sin(p) * 0.03, 1.18 + p * 0.048, -0.45);
      kitchenTable.add(plate);
    }

    // Деревянная табуретка под столом (референс №2)
    const stool = new THREE.Group();
    stool.position.set(-0.85, 0, -0.55);
    const stoolSeat = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.08, 16), woodMat);
    stoolSeat.position.y = 0.58;
    stool.add(stoolSeat);
    [-0.16, 0.16].forEach((sx) => {
      [-0.16, 0.16].forEach((sz) => {
        const sLeg = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.03, 0.58, 8), woodMat);
        sLeg.position.set(sx, 0.29, sz);
        stool.add(sLeg);
      });
    });
    kitchenTable.add(stool);

    this.group.add(kitchenTable);
    const kTableBody = this.physics.world.createRigidBody(R.RigidBodyDesc.fixed().setTranslation(6.8, 1.08, 5.5));
    this.addStaticCollider(R.ColliderDesc.cuboid(1.65, 0.08, 0.95), kTableBody);
    this.hidingZones.push(new THREE.Box3(new THREE.Vector3(5.2, 0, 4.5), new THREE.Vector3(8.4, 0.98, 6.5)));

    // Приоткрытый винтажный холодильник с теплым светом внутри (как на референсе Кухни №2!)
    const fridgeGroup = new THREE.Group();
    fridgeGroup.position.set(10.8, 0, 4.8);
    const fridgeMat = new THREE.MeshPhysicalMaterial({ color: 0xe2e8f0, roughness: 0.25, clearcoat: 0.6 });
    const fridgeBody = new THREE.Mesh(new THREE.BoxGeometry(1.2, 2.3, 1.2), fridgeMat);
    fridgeBody.position.y = 1.15;
    fridgeGroup.add(fridgeBody);

    const fridgeGlow = new THREE.PointLight(0xffea88, 3.2, 6, 1.5);
    fridgeGlow.position.set(-0.75, 1.1, 0.2);
    fridgeGroup.add(fridgeGlow);
    this.group.add(fridgeGroup);

    // 4. Кровать Деда и Книжные шкафы в Кабинете (референс №4)
    const bedGroup = new THREE.Group();
    bedGroup.position.set(this.bedroomPosition.x, 0, this.bedroomPosition.z);
    const bedFrame = new THREE.Mesh(new THREE.BoxGeometry(2.6, 0.5, 3.2), woodMat);
    bedFrame.position.y = 0.25;
    bedGroup.add(bedFrame);

    const blanket = new THREE.Mesh(
      new THREE.BoxGeometry(2.4, 0.35, 2.4),
      createTactileMaterial(0x1d4ed8, { roughness: 0.85 })
    );
    blanket.position.set(0, 0.45, 0.3);
    bedGroup.add(blanket);
    this.group.add(bedGroup);

    const bedBody = this.physics.world.createRigidBody(
      R.RigidBodyDesc.fixed().setTranslation(this.bedroomPosition.x, 0.3, this.bedroomPosition.z)
    );
    this.addStaticCollider(R.ColliderDesc.cuboid(1.3, 0.3, 1.6), bedBody);
  }

  private buildInteractiveObjects() {
    const R = this.physics.getRapier();

    // Деревянные детские кубики с цветными гранями и латунные бубенчики на паркете (как на референсе №3!)
    const woodMat = createWoodParquetMaterial(0xb45309);
    const colors = [0xdc2626, 0x16a34a, 0x2563eb, 0xd97706];

    for (let i = 0; i < 4; i++) {
      const cubeMesh = new THREE.Mesh(new THREE.BoxGeometry(0.26, 0.26, 0.26), woodMat);
      const faceInset = new THREE.Mesh(
        new THREE.BoxGeometry(0.27, 0.18, 0.18),
        createTactileMaterial(colors[i], { roughness: 0.6 })
      );
      cubeMesh.add(faceInset);

      cubeMesh.position.set(-1.4 + i * 0.65, 0.18, 1.8);
      this.group.add(cubeMesh);

      const cubeBody = this.physics.world.createRigidBody(
        R.RigidBodyDesc.dynamic().setTranslation(-1.4 + i * 0.65, 0.18, 1.8)
      );
      this.addStaticCollider(R.ColliderDesc.cuboid(0.13, 0.13, 0.13).setMass(0.25), cubeBody);
      this.interactiveObjects.push({ mesh: cubeMesh, body: cubeBody });
    }

    // Крупные блестящие латунные бубенчики на полу (референс №3)
    const brassBellMat = new THREE.MeshPhysicalMaterial({
      color: 0xfbbf24,
      metalness: 0.9,
      roughness: 0.18,
      clearcoat: 0.8
    });
    [-0.3, 0.1, 0.45].forEach((bx, idx) => {
      const bell = new THREE.Mesh(new THREE.SphereGeometry(0.09, 14, 14), brassBellMat);
      bell.position.set(bx, 0.09, 2.4 + idx * 0.15);
      this.group.add(bell);
    });
  }

  private buildWindow() {
    // Высокие заснеженные окна с холодным синим лунным светом снаружи (как на референсах №2 и №3!)
    const winPositions = [
      [1.8, 2.3, -10.8],
      [7.5, 2.3, -10.8]
    ];

    const frameMat = new THREE.MeshStandardMaterial({ color: 0x1e293b, roughness: 0.6 });
    const glassMat = new THREE.MeshStandardMaterial({
      color: 0x38bdf8,
      emissive: 0x1d4ed8,
      emissiveIntensity: 1.25,
      roughness: 0.15,
      metalness: 0.3
    });
    const curtainMat = createTactileMaterial(0x1e3a5f, { roughness: 0.9 });

    winPositions.forEach((wp) => {
      const winGroup = new THREE.Group();
      winGroup.position.set(wp[0], wp[1], wp[2]);

      const glass = new THREE.Mesh(new THREE.BoxGeometry(2.6, 2.8, 0.06), glassMat);
      winGroup.add(glass);

      // Переплет оконной рамы (решетка)
      for (let r = -1; r <= 1; r++) {
        const hBar = new THREE.Mesh(new THREE.BoxGeometry(2.7, 0.07, 0.12), frameMat);
        hBar.position.y = r * 0.75;
        winGroup.add(hBar);
      }
      [-0.65, 0, 0.65].forEach((cx) => {
        const vBar = new THREE.Mesh(new THREE.BoxGeometry(0.07, 2.85, 0.12), frameMat);
        vBar.position.x = cx;
        winGroup.add(vBar);
      });

      // Тяжелые синие шторы по бокам окна (референс №3)
      [-1.45, 1.45].forEach((sx) => {
        const curtain = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.28, 3.4, 12), curtainMat);
        curtain.position.set(sx, -0.2, 0.15);
        winGroup.add(curtain);
      });

      // Лунный свет без PointLight: мягкое светящееся пятно на полу
      const moonPool = new THREE.Mesh(
        new THREE.PlaneGeometry(2.4, 3.4),
        new THREE.MeshBasicMaterial({
          color: 0x4f7fd0,
          transparent: true,
          opacity: 0.16,
          depthWrite: false,
          blending: THREE.AdditiveBlending
        })
      );
      moonPool.rotation.x = -Math.PI / 2;
      moonPool.position.set(0, -wp[1] + 0.05, 2.2);
      winGroup.add(moonPool);

      this.group.add(winGroup);
    });
  }

  public update(
    delta: number,
    gripLevel: number = 0
  ): {
    noiseFromClatter: number;
    deliveredCount: number;
    totalGiftsCount: number;
    allDelivered: boolean;
  } {
    let clatterNoise = 0;
    this.clockTimer += delta;

    // Плавное качание маятника часов и живое мерцание огня в камине
    if (this.pendulumMesh) {
      this.pendulumMesh.position.x = Math.sin(this.clockTimer * 3.2) * 0.16;
    }
    if (this.fireLight) {
      this.fireLight.intensity = 5.2 + Math.sin(this.clockTimer * 14) * 0.6 + Math.cos(this.clockTimer * 23) * 0.4;
    }
    this.fireFlames.forEach((f, idx) => {
      f.scale.y = 0.85 + Math.sin(this.clockTimer * 12 + idx * 1.7) * 0.22;
    });

    let delivered = 0;
    const target = new THREE.Vector3(this.treePosition.x + 0.5, 0, this.treePosition.z + 1.4);

    for (const gift of this.gifts) {
      const { noiseGenerated } = gift.update(delta, gripLevel);
      if (noiseGenerated > 0) {
        clatterNoise = Math.max(clatterNoise, noiseGenerated);
      }

      if (!gift.isInSack) {
        const gPos = gift.mesh.position;
        const dist = Math.hypot(gPos.x - target.x, gPos.z - target.z);
        if (dist < 2.2 && gPos.y < 1.2) {
          gift.isDelivered = true;
          delivered++;
        } else {
          gift.isDelivered = false;
        }
      } else {
        gift.isDelivered = false;
      }
    }

    const totalGiftsCount = this.gifts.length;
    const allDelivered = totalGiftsCount > 0 && delivered >= totalGiftsCount;

    if (this.exitRingMesh) {
      const mat = this.exitRingMesh.material as THREE.MeshBasicMaterial;
      mat.color.setHex(allDelivered ? 0x10b981 : 0x38bdf8);
    }

    for (const obj of this.interactiveObjects) {
      const pos = obj.body.translation();
      const rot = obj.body.rotation();
      obj.mesh.position.set(pos.x, pos.y, pos.z);
      obj.mesh.quaternion.set(rot.x, rot.y, rot.z, rot.w);

      const vel = obj.body.linvel();
      const speed = Math.hypot(vel.x, vel.y, vel.z);
      if (speed > 2.2 && !obj.isBroken && pos.y < 0.25) {
        this.soundEngine.playClatter();
        clatterNoise = Math.max(clatterNoise, 60);
        obj.isBroken = true;
      }
    }

    return {
      noiseFromClatter: clatterNoise,
      deliveredCount: delivered,
      totalGiftsCount,
      allDelivered
    };
  }

  public getNearestGift(elfPos: THREE.Vector3, maxDist: number = 2.3): GiftItem | null {
    let nearest: GiftItem | null = null;
    let minDist = maxDist;

    for (const gift of this.gifts) {
      if (gift.isInSack) continue;
      const dist = gift.mesh.position.distanceTo(elfPos);
      if (dist < minDist) {
        minDist = dist;
        nearest = gift;
      }
    }
    return nearest;
  }

  public isNearFireplaceExit(elfPos: THREE.Vector3): boolean {
    const exitPos = new THREE.Vector3(this.fireplacePosition.x + 1.8, 0, this.fireplacePosition.z);
    return Math.hypot(elfPos.x - exitPos.x, elfPos.z - exitPos.z) < 2.1;
  }

  public getSurfaceType(pos: THREE.Vector3): 'wood' | 'carpet' | 'tile' | 'ice' {
    for (const zone of this.iceZones) {
      if (zone.containsPoint(pos)) return 'ice';
    }
    for (const zone of this.tileZones) {
      if (zone.containsPoint(pos)) return 'tile';
    }
    for (const zone of this.carpetZones) {
      if (zone.containsPoint(pos)) return 'carpet';
    }
    return 'wood';
  }

  public isCreaky(pos: THREE.Vector3): boolean {
    for (const zone of this.creakyZones) {
      if (zone.containsPoint(pos)) return true;
    }
    return false;
  }

  public isHidden(pos: THREE.Vector3): boolean {
    for (const zone of this.hidingZones) {
      if (zone.containsPoint(pos)) return true;
    }
    return false;
  }

  public setVisible(visible: boolean) {
    this.group.visible = visible;
    for (const c of this.colliders) {
      c.setEnabled(visible);
    }
    for (const obj of this.interactiveObjects) {
      obj.mesh.visible = visible;
    }
    for (const g of this.gifts) {
      g.setVisible(visible);
    }
  }
}
