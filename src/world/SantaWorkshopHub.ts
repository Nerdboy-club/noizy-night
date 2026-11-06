import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { PhysicsWorld } from '../physics/PhysicsWorld';
import { SoundEngine } from '../audio/SoundEngine';
import { GiftItem, GiftType } from '../entities/GiftItem';
import {
  createBrickMaterial,
  createChristmasRugMaterial,
  createWoodParquetMaterial
} from '../rendering/TactileMaterials';
import { createPeakMaterial } from '../rendering/ToonStyle';

export interface DeliveryOrder {
  id: string;
  address: string;
  description: string;
  isSpookyHouse: boolean; // true = редкий темный/жуткий дом с двойными чаевыми!
  tipMultiplier: number;
  requiredGifts: GiftType[];
  baseReward: number;
}

export class SantaWorkshopHub {
  public group: THREE.Group;
  public gifts: GiftItem[] = [];
  public spawnPoint: THREE.Vector3 = new THREE.Vector3(0, 0.8, 0.5);
  public conveyorPosition: THREE.Vector3 = new THREE.Vector3(-5.2, 0, -1.5);
  public workbenchPosition: THREE.Vector3 = new THREE.Vector3(5.2, 0, -1.5);
  public orderBoardPosition: THREE.Vector3 = new THREE.Vector3(0, 0, -5.8);
  public sleighLaunchPosition: THREE.Vector3 = new THREE.Vector3(0, 0, 4.5);

  public availableOrders: DeliveryOrder[] = [];
  public activeOrder: DeliveryOrder;

  private scene: THREE.Scene;
  private physics: PhysicsWorld;
  private soundEngine: SoundEngine;
  private colliders: RAPIER.Collider[] = [];
  private bodies: RAPIER.RigidBody[] = [];
  private launchRingMesh!: THREE.Mesh;

  constructor(scene: THREE.Scene, physics: PhysicsWorld, soundEngine: SoundEngine) {
    this.scene = scene;
    this.physics = physics;
    this.soundEngine = soundEngine;
    this.group = new THREE.Group();

    this.generateNewOrders();
    this.activeOrder = this.availableOrders[0];

    this.buildWorkshopGeometry();
    this.spawnOrderGiftsOnConveyor();

    this.group.traverse((child) => {
      if ((child as THREE.Mesh).isMesh) {
        child.castShadow = true;
        child.receiveShadow = true;
      }
    });

    this.scene.add(this.group);
  }

  public generateNewOrders() {
    const streetNames = [
      'ул. Снежная 7 (Уютный коттедж)',
      'пр. Рождественских Елей 24 (Двухэтажный дом Деда)',
      'пер. Пряничный 11 (Дом с камином и котом)',
      'бул. Северного Сияния 3 (Особняк со скользкой плиткой)'
    ];
    const spookyNames = [
      'пер. Старых Часов 13 (ЖУТКИЙ ТЁМНЫЙ ДОМ 👻)',
      'тупик Крампуса 66 (МРАЧНЫЙ ОСОБНЯК 🕯️)'
    ];

    const pick1 = streetNames[Math.floor(Math.random() * streetNames.length)];
    const pick2 = streetNames[(Math.floor(Math.random() * streetNames.length) + 1) % streetNames.length];
    const pickSpooky = spookyNames[Math.floor(Math.random() * spookyNames.length)];

    this.availableOrders = [
      {
        id: 'order-cozy-1',
        address: pick1,
        description: 'Уютная праздничная ночь, гирлянды и тёплый камин. Доставьте 3 подарка (включая Велосипед в обёртке) под ёлку!',
        isSpookyHouse: false,
        tipMultiplier: 1.0,
        requiredGifts: ['SMALL', 'NOISY', 'HUGE'],
        baseReward: 160
      },
      {
        id: 'order-cozy-2',
        address: pick2,
        description: 'Большой 5-комнатный дом (Гостиная, Прихожая с часами, Кухня с плиткой, Кабинет и Гараж со льдом). Ворчливый Дед чутко спит!',
        isSpookyHouse: false,
        tipMultiplier: 1.35,
        requiredGifts: ['SMALL', 'SMALL', 'NOISY', 'HUGE'],
        baseReward: 240
      },
      {
        id: 'order-spooky-3',
        address: pickSpooky,
        description: 'РЕДКИЙ ЗАКАЗ (ЧАЕВЫЕ x2.0!): В доме погас свет, в коридоре бьют старинные часы, а в тенях бродит нечто жуткое...',
        isSpookyHouse: true,
        tipMultiplier: 2.0,
        requiredGifts: ['SMALL', 'NOISY', 'HUGE'],
        baseReward: 340
      }
    ];
  }

  public selectOrder(orderId: string) {
    const found = this.availableOrders.find((o) => o.id === orderId);
    if (found) {
      this.activeOrder = found;
      this.spawnOrderGiftsOnConveyor();
    }
  }

  public spawnOrderGiftsOnConveyor() {
    // Удаляем старые подарки на конвейере
    for (const g of this.gifts) {
      this.scene.remove(g.mesh);
      try {
        this.physics.world.removeRigidBody(g.body);
      } catch {}
    }
    this.gifts = [];

    // Спавним подарки по текущему заказу возле Конвейера Фабрики
    const req = this.activeOrder.requiredGifts;
    req.forEach((type, idx) => {
      const spawnPos = new THREE.Vector3(
        this.conveyorPosition.x + 1.2 + (idx % 2) * 0.9,
        0.75,
        this.conveyorPosition.z - 1.2 + idx * 1.1
      );
      const gift = new GiftItem(
        `hub-gift-${idx}-${Date.now()}`,
        type,
        spawnPos,
        this.scene,
        this.physics,
        this.soundEngine
      );
      gift.setVisible(this.group.visible);
      this.gifts.push(gift);
    });
  }

  // ---- visual helpers (shared geometry / materials to keep draw calls low) ----
  private geoCache = new Map<string, THREE.BufferGeometry>();
  private matCache = new Map<string, THREE.Material>();

  private box(w: number, h: number, d: number): THREE.BufferGeometry {
    const k = `b${w}_${h}_${d}`;
    let g = this.geoCache.get(k);
    if (!g) { g = new THREE.BoxGeometry(w, h, d); this.geoCache.set(k, g); }
    return g;
  }
  private cyl(rt: number, rb: number, h: number, seg = 12): THREE.BufferGeometry {
    const k = `c${rt}_${rb}_${h}_${seg}`;
    let g = this.geoCache.get(k);
    if (!g) { g = new THREE.CylinderGeometry(rt, rb, h, seg); this.geoCache.set(k, g); }
    return g;
  }
  private sph(r: number, seg = 10): THREE.BufferGeometry {
    const k = `s${r}_${seg}`;
    let g = this.geoCache.get(k);
    if (!g) { g = new THREE.SphereGeometry(r, seg, seg); this.geoCache.set(k, g); }
    return g;
  }
  private tactile(color: number, opts?: { emissive?: number; emissiveIntensity?: number; roughness?: number; metalness?: number; knitBump?: boolean }): THREE.Material {
    const k = `t${color}_${opts?.emissive ?? ''}_${opts?.emissiveIntensity ?? ''}_${opts?.roughness ?? ''}_${opts?.metalness ?? ''}_${opts?.knitBump ?? ''}`;
    let m = this.matCache.get(k);
    if (!m) { m = createPeakMaterial(color, opts); this.matCache.set(k, m); }
    return m;
  }
  private metal(color: number, rough = 0.35): THREE.Material {
    const k = `m${color}_${rough}`;
    let m = this.matCache.get(k);
    if (!m) { m = new THREE.MeshStandardMaterial({ color, metalness: 0.85, roughness: rough }); this.matCache.set(k, m); }
    return m;
  }
  private glow(color: number, intensity = 1.6): THREE.Material {
    const k = `g${color}_${intensity}`;
    let m = this.matCache.get(k);
    if (!m) { m = new THREE.MeshStandardMaterial({ color, emissive: color, emissiveIntensity: intensity, roughness: 0.5 }); this.matCache.set(k, m); }
    return m;
  }
  private put(parent: THREE.Object3D, geo: THREE.BufferGeometry, mat: THREE.Material,
    x: number, y: number, z: number, rx = 0, ry = 0, rz = 0): THREE.Mesh {
    const m = new THREE.Mesh(geo, mat);
    m.position.set(x, y, z);
    m.rotation.set(rx, ry, rz);
    parent.add(m);
    return m;
  }

  /** Wrapped gift box with cross ribbon and bow (decor only). */
  private addGiftBox(parent: THREE.Object3D, x: number, y: number, z: number, s: number,
    boxCol: number, ribCol: number, ry = 0) {
    const g = new THREE.Group();
    g.position.set(x, y, z);
    g.rotation.y = ry;
    const bm = this.tactile(boxCol, { roughness: 0.7 });
    const rm = this.tactile(ribCol, { roughness: 0.45, knitBump: false });
    this.put(g, this.box(s, s, s), bm, 0, s / 2, 0);
    this.put(g, this.box(s * 0.18, s * 1.02, s * 1.02), rm, 0, s / 2, 0);
    this.put(g, this.box(s * 1.02, s * 1.02, s * 0.18), rm, 0, s / 2, 0);
    this.put(g, this.sph(s * 0.13, 8), rm, -s * 0.13, s * 1.08, 0);
    this.put(g, this.sph(s * 0.13, 8), rm, s * 0.13, s * 1.08, 0);
    parent.add(g);
  }

  /** Snow-lit window on a wall. (x,y,z) is the inner wall surface; ry faces into the room. */
  private addWindow(x: number, y: number, z: number, ry: number, w = 1.8, h = 1.6) {
    const g = new THREE.Group();
    g.position.set(x, y, z);
    g.rotation.y = ry;
    const frame = this.tactile(0x5a3418, { roughness: 0.8, knitBump: false });
    const pane = this.glow(0xaed8ff, 1.1);
    const snow = this.tactile(0xf4f8ff, { roughness: 0.95, knitBump: false });
    this.put(g, this.box(w - 0.2, h - 0.2, 0.06), pane, 0, 0, 0.02);
    this.put(g, this.box(w + 0.2, 0.12, 0.16), frame, 0, h / 2 + 0.04, 0.08);
    this.put(g, this.box(w + 0.2, 0.12, 0.16), frame, 0, -h / 2 - 0.04, 0.08);
    this.put(g, this.box(0.12, h + 0.2, 0.16), frame, -w / 2 - 0.04, 0, 0.08);
    this.put(g, this.box(0.12, h + 0.2, 0.16), frame, w / 2 + 0.04, 0, 0.08);
    this.put(g, this.box(0.08, h, 0.1), frame, 0, 0, 0.08);
    this.put(g, this.box(w, 0.08, 0.1), frame, 0, 0, 0.08);
    // sill with snow drift
    this.put(g, this.box(w + 0.45, 0.1, 0.34), frame, 0, -h / 2 - 0.14, 0.2);
    this.put(g, this.box(w + 0.25, 0.1, 0.26), snow, 0, -h / 2 - 0.05, 0.2);
    [-w * 0.3, 0, w * 0.3].forEach((dx, i) => this.put(g, this.sph(0.16 + i % 2 * 0.04, 8), snow, dx, -h / 2 - 0.02, 0.2));
    // curtains
    const curt = this.tactile(0xb91c1c, { roughness: 0.9 });
    this.put(g, this.box(0.3, h + 0.1, 0.1), curt, -w / 2 + 0.1, 0.02, 0.16);
    this.put(g, this.box(0.3, h + 0.1, 0.1), curt, w / 2 - 0.1, 0.02, 0.16);
    this.put(g, this.cyl(0.03, 0.03, w + 0.5, 6), this.metal(0xd4a017), 0, h / 2 + 0.2, 0.18, 0, 0, Math.PI / 2);
    this.group.add(g);
  }

  private addWreath(x: number, y: number, z: number, ry: number) {
    const g = new THREE.Group();
    g.position.set(x, y, z);
    g.rotation.y = ry;
    this.put(g, new THREE.TorusGeometry(0.42, 0.13, 8, 20), this.tactile(0x166534, { roughness: 0.9 }), 0, 0, 0.1);
    this.put(g, this.box(0.34, 0.14, 0.08), this.tactile(0xdc2626), -0.15, -0.42, 0.2, 0, 0, 0.4);
    this.put(g, this.box(0.34, 0.14, 0.08), this.tactile(0xdc2626), 0.15, -0.42, 0.2, 0, 0, -0.4);
    this.put(g, this.sph(0.09, 8), this.tactile(0xb91c1c), 0, -0.42, 0.22);
    this.group.add(g);
  }

  private addTree(x: number, z: number, h = 3.2) {
    const g = new THREE.Group();
    g.position.set(x, 0, z);
    const needle = this.tactile(0x1f6f3a, { roughness: 0.92 });
    this.put(g, this.cyl(0.12, 0.16, 0.5, 8), this.tactile(0x4a2a12), 0, 0.25, 0);
    this.put(g, this.cyl(0.55, 0.65, 0.35, 12), this.tactile(0xb91c1c), 0, 0.18, 0);
    const tiers = 4;
    for (let i = 0; i < tiers; i++) {
      const r = 1.2 - i * 0.26;
      const th = h / tiers + 0.5;
      this.put(g, new THREE.ConeGeometry(r, th, 12), needle, 0, 0.6 + i * (h / tiers) * 0.78 + th / 2, 0);
    }
    this.put(g, new THREE.OctahedronGeometry(0.2), this.glow(0xfde047, 2.4), 0, h + 0.55, 0);
    // ornaments (instanced)
    const cols = [0xef4444, 0xfacc15, 0x38bdf8, 0xf472b6];
    const n = 22;
    const inst = new THREE.InstancedMesh(this.sph(0.075, 8), new THREE.MeshStandardMaterial({ color: 0xffffff, metalness: 0.6, roughness: 0.25 }), n);
    const mtx = new THREE.Matrix4();
    for (let i = 0; i < n; i++) {
      const t = (i + 0.5) / n;
      const yy = 0.9 + t * (h - 0.7);
      const rr = (1.15 - t * 1.0) * 0.82;
      const a = i * 2.4;
      mtx.setPosition(Math.cos(a) * rr, yy, Math.sin(a) * rr);
      inst.setMatrixAt(i, mtx);
      inst.setColorAt(i, new THREE.Color(cols[i % cols.length]));
    }
    g.add(inst);
    this.addGiftBox(g, 0.9, 0, 0.7, 0.45, 0x2563eb, 0xfacc15, 0.4);
    this.addGiftBox(g, -0.8, 0, 0.9, 0.38, 0xdc2626, 0xf8fafc, -0.3);
    this.group.add(g);
  }

  private addToyShelf(x: number, z: number, ry: number, len = 3.6) {
    const g = new THREE.Group();
    g.position.set(x, 0, z);
    g.rotation.y = ry;
    const wood = this.tactile(0x6b3a1a, { roughness: 0.8, knitBump: false });
    const levels = [0.55, 1.4, 2.25];
    [-len / 2, len / 2].forEach((px) => this.put(g, this.box(0.1, 2.6, 0.5), wood, px, 1.3, 0));
    this.put(g, this.box(len, 2.6, 0.06), this.tactile(0x4a2610, { knitBump: false }), 0, 1.3, -0.22);
    levels.forEach((ly) => this.put(g, this.box(len, 0.08, 0.5), wood, 0, ly, 0));
    this.put(g, this.box(len + 0.1, 0.08, 0.56), wood, 0, 2.62, 0);
    const toyCols = [0xef4444, 0x3b82f6, 0xfacc15, 0x22c55e, 0xf472b6, 0xa855f7, 0xfb923c];
    const toys = new THREE.InstancedMesh(this.box(0.3, 0.3, 0.3), new THREE.MeshStandardMaterial({ roughness: 0.6 }), levels.length * 8);
    const balls = new THREE.InstancedMesh(this.sph(0.15, 10), new THREE.MeshStandardMaterial({ roughness: 0.4 }), levels.length * 4);
    const m4 = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const one = new THREE.Vector3(1, 1, 1);
    let ti = 0, bi = 0;
    levels.forEach((ly, li) => {
      for (let i = 0; i < 8; i++) {
        const s = 0.7 + ((i * 7 + li * 3) % 5) * 0.12;
        q.setFromEuler(new THREE.Euler(0, ((i + li) % 3 - 1) * 0.25, 0));
        m4.compose(new THREE.Vector3(-len / 2 + 0.3 + i * (len - 0.6) / 7.5, ly + 0.04 + 0.15 * s, 0), q, new THREE.Vector3(s, s, s));
        toys.setMatrixAt(ti, m4);
        toys.setColorAt(ti++, new THREE.Color(toyCols[(i * 3 + li) % toyCols.length]));
      }
      for (let i = 0; i < 4; i++) {
        m4.compose(new THREE.Vector3(-len / 2 + 0.55 + i * (len - 1) / 3.2 + 0.2, ly + 0.19, 0.12), q.identity(), one);
        balls.setMatrixAt(bi, m4);
        balls.setColorAt(bi++, new THREE.Color(toyCols[(i * 2 + li + 2) % toyCols.length]));
      }
    });
    g.add(toys, balls);
    this.group.add(g);
  }

  private buildWorkshopGeometry() {
    const R = this.physics.getRapier();
    const walnut = this.tactile(0x4a2610, { roughness: 0.75, knitBump: false });
    const oak = this.tactile(0x9a5a2a, { roughness: 0.7, knitBump: false });
    const dark = this.tactile(0x2b1a10, { roughness: 0.8, knitBump: false });
    const red = this.tactile(0xc62828);
    const cream = this.tactile(0xfff1d6, { roughness: 0.9 });
    const gold = this.metal(0xe0a82e, 0.3);
    const steel = this.metal(0x94a3b8, 0.4);

    // 1. Floor: lighter warm parquet so the player pops, framed by a dark border; knitted rug on top
    const floor = new THREE.Mesh(new THREE.BoxGeometry(16, 0.4, 16), createWoodParquetMaterial(0xa5622b));
    floor.position.set(0, -0.2, 0);
    floor.receiveShadow = true;
    this.group.add(floor);

    const floorBody = this.physics.world.createRigidBody(R.RigidBodyDesc.fixed().setTranslation(0, -0.2, 0));
    const floorCol = this.physics.world.createCollider(R.ColliderDesc.cuboid(8, 0.2, 8), floorBody);
    this.bodies.push(floorBody);
    this.colliders.push(floorCol);

    const rug = new THREE.Mesh(new THREE.BoxGeometry(7.5, 0.04, 7.5), createChristmasRugMaterial());
    rug.position.set(0, 0.02, -0.5);
    this.group.add(rug);
    // rug gold trim
    const trimMat = this.tactile(0xe8b84a, { roughness: 0.6, knitBump: false });
    this.put(this.group, this.box(7.7, 0.03, 0.1), trimMat, 0, 0.03, -4.35);
    this.put(this.group, this.box(7.7, 0.03, 0.1), trimMat, 0, 0.03, 3.35);
    this.put(this.group, this.box(0.1, 0.03, 7.5), trimMat, -3.85, 0.03, -0.5);
    this.put(this.group, this.box(0.1, 0.03, 7.5), trimMat, 3.85, 0.03, -0.5);

    // 2. Walls: warm brick above, dark walnut wainscot, crown/baseboard trim
    const wallMat = createBrickMaterial(0x9a4220);
    const wainscotMat = createWoodParquetMaterial(0x5b3014);
    const walls = [
      { size: [16, 4.5, 0.4], pos: [0, 2.25, -8], ry: 0 },
      { size: [16, 4.5, 0.4], pos: [0, 2.25, 8], ry: Math.PI },
      { size: [0.4, 4.5, 16], pos: [-8, 2.25, 0], ry: Math.PI / 2 },
      { size: [0.4, 4.5, 16], pos: [8, 2.25, 0], ry: -Math.PI / 2 }
    ];
    walls.forEach((w) => {
      const m = new THREE.Mesh(new THREE.BoxGeometry(w.size[0], w.size[1], w.size[2]), wallMat);
      m.position.set(w.pos[0], w.pos[1], w.pos[2]);
      m.receiveShadow = true;
      this.group.add(m);

      const panel = new THREE.Mesh(new THREE.BoxGeometry(w.size[0] + 0.05, 1.15, w.size[2] + 0.05), wainscotMat);
      panel.position.set(w.pos[0], 0.575, w.pos[2]);
      this.group.add(panel);

      // inward offset for trim (towards room centre)
      const nx = w.pos[0] === 0 ? 0 : -Math.sign(w.pos[0]);
      const nz = w.pos[2] === 0 ? 0 : -Math.sign(w.pos[2]);
      const alongX = w.size[0] > w.size[2];
      const L = alongX ? w.size[0] : w.size[2];
      const ox = w.pos[0] + nx * 0.22;
      const oz = w.pos[2] + nz * 0.22;
      const dims = (t: number, h: number): [number, number, number] => (alongX ? [L, h, t] : [t, h, L]);
      const rail = dims(0.14, 0.1);
      this.put(this.group, this.box(...rail), oak, ox, 1.2, oz);
      const base = dims(0.12, 0.2);
      this.put(this.group, this.box(...base), dark, ox, 0.1, oz);
      const crown = dims(0.2, 0.2);
      this.put(this.group, this.box(...crown), walnut, ox, 4.4, oz);

      const b = this.physics.world.createRigidBody(R.RigidBodyDesc.fixed().setTranslation(w.pos[0], w.pos[1], w.pos[2]));
      const c = this.physics.world.createCollider(R.ColliderDesc.cuboid(w.size[0] / 2, w.size[1] / 2, w.size[2] / 2), b);
      this.bodies.push(b);
      this.colliders.push(c);
    });

    // Ceiling beams (decor, above play height)
    for (let i = 0; i < 5; i++) {
      this.put(this.group, this.box(16, 0.3, 0.3), walnut, 0, 4.35, -6.4 + i * 3.2);
    }
    for (let i = 0; i < 2; i++) {
      this.put(this.group, this.box(0.3, 0.3, 16), walnut, -4 + i * 8, 4.55, 0);
    }

    // 3. Windows with snow light, wreaths
    this.addWindow(-5.4, 2.6, -7.78, 0);
    this.addWindow(5.4, 2.6, -7.78, 0);
    this.addWindow(-7.78, 2.6, 4.0, Math.PI / 2, 1.6, 1.5);
    this.addWindow(7.78, 2.6, 4.0, -Math.PI / 2, 1.6, 1.5);
    this.addWindow(-7.78, 2.6, -4.0, Math.PI / 2, 1.6, 1.5);
    this.addWindow(7.78, 2.6, -4.0, -Math.PI / 2, 1.6, 1.5);
    this.addWreath(-3.0, 2.9, -7.75, 0);
    this.addWreath(3.0, 2.9, -7.75, 0);
    this.addWreath(-3.2, 3.0, 7.75, Math.PI);
    this.addWreath(3.2, 3.0, 7.75, Math.PI);

    // 4. Garlands: sagging green strand + instanced bulbs on all 4 walls
    const bulbColors = [0xff2d55, 0xfacc15, 0x22c55e, 0x38bdf8, 0xf97316];
    const perWall = 16;
    const bulbs = new THREE.InstancedMesh(this.sph(0.1, 8), new THREE.MeshBasicMaterial({ color: 0xffffff }), perWall * 4);
    const leaves = new THREE.InstancedMesh(this.sph(0.13, 6), this.tactile(0x1f7a3d, { roughness: 0.95 }), 48 * 4);
    const mm = new THREE.Matrix4();
    let bi = 0, li = 0;
    const garland = (ax: number, az: number, dx: number, dz: number, ix: number, iz: number) => {
      for (let i = 0; i < 48; i++) {
        const t = i / 47;
        const sag = Math.sin(t * Math.PI * 6) ; // 3 swags
        const y = 3.85 - Math.abs(sag) * 0.35;
        mm.setPosition(ax + dx * t + ix, y, az + dz * t + iz);
        leaves.setMatrixAt(li++, mm);
      }
      for (let i = 0; i < perWall; i++) {
        const t = (i + 0.5) / perWall;
        const sag = Math.sin(t * Math.PI * 6);
        const y = 3.85 - Math.abs(sag) * 0.35 - 0.14;
        mm.setPosition(ax + dx * t + ix, y, az + dz * t + iz);
        bulbs.setMatrixAt(bi, mm);
        bulbs.setColorAt(bi++, new THREE.Color(bulbColors[i % bulbColors.length]));
      }
    };
    garland(-7.5, -7.55, 15, 0, 0, 0);
    garland(-7.5, 7.55, 15, 0, 0, 0);
    garland(-7.55, -7.5, 0, 15, 0, 0);
    garland(7.55, -7.5, 0, 15, 0, 0);
    this.group.add(leaves, bulbs);

    // Hanging star lanterns over the rug (emissive only, no lights)
    [[-2.5, -2], [2.5, -2], [-2.5, 1.2], [2.5, 1.2]].forEach(([lx, lz], i) => {
      const col = [0xfde047, 0xfb7185, 0x7dd3fc, 0x86efac][i];
      this.put(this.group, this.cyl(0.01, 0.01, 0.8, 4), dark, lx, 4.0, lz);
      this.put(this.group, new THREE.OctahedronGeometry(0.22), this.glow(col, 2.2), lx, 3.45, lz);
    });

    // 5. ZONE A (left): CONVEYOR
    const convGroup = new THREE.Group();
    convGroup.position.copy(this.conveyorPosition);

    this.put(convGroup, this.box(1.8, 0.2, 5.2), this.metal(0x3b4a5e, 0.5), -0.6, 0.3, 0);
    this.put(convGroup, this.box(1.5, 0.08, 5.0), this.tactile(0x1e293b, { roughness: 0.9, knitBump: false }), -0.6, 0.42, 0);
    [-1.45, 0.25].forEach((rx) => {
      this.put(convGroup, this.box(0.12, 0.26, 5.2), steel, rx, 0.5, 0);
      this.put(convGroup, this.box(0.14, 0.04, 5.2), red, rx, 0.64, 0);
    });
    [-1.4, 1.4].forEach((lz) => [-1.4, 0.2].forEach((lx) => this.put(convGroup, this.box(0.14, 0.2, 0.14), dark, lx, 0.1, lz)));
    const rollers = new THREE.InstancedMesh(this.cyl(0.07, 0.07, 1.45, 8), steel, 14);
    for (let i = 0; i < 14; i++) {
      const o = new THREE.Object3D();
      o.position.set(-0.6, 0.48, -2.3 + i * 0.355);
      o.rotation.z = Math.PI / 2;
      o.updateMatrix();
      rollers.setMatrixAt(i, o.matrix);
    }
    convGroup.add(rollers);
    // hazard stripes on belt edge
    const stripeA = this.tactile(0xfacc15, { knitBump: false });
    for (let i = 0; i < 10; i++) this.put(convGroup, this.box(0.05, 0.05, 0.25), i % 2 ? dark : stripeA, 0.27, 0.66, -2.3 + i * 0.5);

    // gift chute (red hopper w/ trim)
    this.put(convGroup, this.box(1.8, 2.4, 1.4), red, -0.8, 1.2, -2.0);
    this.put(convGroup, this.box(1.9, 0.15, 1.5), gold, -0.8, 2.45, -2.0);
    this.put(convGroup, this.box(1.9, 0.12, 1.5), gold, -0.8, 0.1, -2.0);
    this.put(convGroup, this.box(1.3, 1.1, 0.06), dark, -0.8, 0.95, -1.28);
    this.put(convGroup, this.box(1.3, 0.08, 0.7), this.tactile(0x64748b, { knitBump: false }), -0.8, 0.45, -1.0, -0.35, 0, 0);
    this.put(convGroup, this.cyl(0.35, 0.35, 0.06, 16), cream, -0.8, 1.9, -1.28, Math.PI / 2, 0, 0);
    this.put(convGroup, this.box(0.04, 0.25, 0.04), dark, -0.8, 1.95, -1.25);
    this.put(convGroup, this.box(0.2, 0.04, 0.04), dark, -0.72, 1.9, -1.25);
    // little status lamps
    [0xef4444, 0xfacc15, 0x22c55e].forEach((c, i) => this.put(convGroup, this.sph(0.07, 8), this.glow(c, 2.4), -1.25 + i * 0.25, 2.2, -1.27));

    const convLight = new THREE.PointLight(0xffb347, 1.8, 6);
    convLight.position.set(0.5, 2.5, 0);
    convGroup.add(convLight);
    this.group.add(convGroup);

    // 6. ZONE B (back centre): ORDER BOARD
    const boardGroup = new THREE.Group();
    boardGroup.position.copy(this.orderBoardPosition);

    this.put(boardGroup, this.box(3.8, 2.2, 0.25), this.tactile(0x6b3a1a, { roughness: 0.75, knitBump: false }), 0, 1.7, 0);
    this.put(boardGroup, this.box(3.4, 1.8, 0.28), this.tactile(0xc98a4b, { roughness: 0.95 }), 0, 1.7, 0.02);
    this.put(boardGroup, this.box(4.1, 0.14, 0.34), gold, 0, 2.88, 0);
    // Lamps over the board
    [-1.4, 1.4].forEach((lx) => {
      this.put(boardGroup, this.box(0.1, 0.1, 0.5), gold, lx, 2.98, 0.2);
      this.put(boardGroup, this.cyl(0.05, 0.22, 0.22, 10), this.metal(0x14532d, 0.5), lx, 2.95, 0.5);
      this.put(boardGroup, this.sph(0.09, 8), this.glow(0xffe29a, 3), lx, 2.82, 0.5);
    });
    [-1.0, 0, 1.0].forEach((px, idx) => {
      const paperCol = idx === 2 ? 0xa855f7 : 0xfef08a;
      const paper = new THREE.Mesh(
        new THREE.BoxGeometry(0.85, 1.1, 0.05),
        new THREE.MeshStandardMaterial({ color: paperCol, emissive: paperCol, emissiveIntensity: 0.3 })
      );
      paper.position.set(px, 1.7, 0.16);
      paper.rotation.z = (idx - 1) * 0.06;
      boardGroup.add(paper);
      this.put(boardGroup, this.sph(0.05, 6), this.metal(0xdc2626, 0.3), px, 2.2, 0.2);
      for (let l = 0; l < 4; l++) this.put(boardGroup, this.box(0.55, 0.04, 0.01), dark, px, 1.95 - l * 0.22, 0.2, 0, 0, paper.rotation.z);
    });
    this.group.add(boardGroup);

    // 7. ZONE V (right): WORKBENCH
    const benchGroup = new THREE.Group();
    benchGroup.position.copy(this.workbenchPosition);

    this.put(benchGroup, this.box(1.8, 0.85, 4.2), this.tactile(0x8a4a22, { roughness: 0.75, knitBump: false }), 0.6, 0.42, 0);
    this.put(benchGroup, this.box(2.0, 0.12, 4.4), oak, 0.6, 0.9, 0);
    this.put(benchGroup, this.box(1.7, 0.6, 4.0), dark, 0.6, 0.38, 0); // dark apron under top
    [-1.6, 0, 1.6].forEach((dz) => {
      this.put(benchGroup, this.box(0.04, 0.45, 1.1), this.tactile(0x8a4a22, { knitBump: false }), -0.31, 0.4, dz);
      this.put(benchGroup, this.sph(0.04, 6), gold, -0.35, 0.4, dz);
    });
    const demoJetpack = new THREE.Mesh(
      new THREE.CylinderGeometry(0.18, 0.18, 0.55, 10),
      new THREE.MeshStandardMaterial({ color: 0x0284c7, emissive: 0x0284c7, emissiveIntensity: 0.4 })
    );
    demoJetpack.position.set(0.5, 1.2, -0.8);
    benchGroup.add(demoJetpack);
    this.put(benchGroup, this.cyl(0.12, 0.17, 0.12, 10), this.glow(0xfb923c, 2), 0.5, 0.92, -0.8);
    this.put(benchGroup, this.cyl(0.2, 0.2, 0.06, 10), steel, 0.5, 1.5, -0.8);
    const demoBoot = new THREE.Mesh(
      new THREE.BoxGeometry(0.35, 0.35, 0.55),
      new THREE.MeshStandardMaterial({ color: 0x10b981, emissive: 0x10b981, emissiveIntensity: 0.3 })
    );
    demoBoot.position.set(0.5, 1.1, 0.8);
    benchGroup.add(demoBoot);
    this.put(benchGroup, this.box(0.4, 0.1, 0.62), this.tactile(0xf8fafc), 0.5, 0.97, 0.8);
    // tools: hammer, wrench, gears, vise, lamp
    this.put(benchGroup, this.box(0.06, 0.06, 0.5), this.tactile(0x7c4a1e, { knitBump: false }), 0.9, 1.0, 0.0, 0, 0.4, 0);
    this.put(benchGroup, this.box(0.22, 0.12, 0.1), steel, 0.98, 1.04, -0.2, 0, 0.4, 0);
    this.put(benchGroup, this.box(0.07, 0.03, 0.45), steel, 1.1, 0.97, 0.9, 0, -0.3, 0);
    this.put(benchGroup, this.cyl(0.1, 0.1, 0.04, 8), steel, 0.2, 0.98, 0.1, 0, 0, 0);
    this.put(benchGroup, this.cyl(0.07, 0.07, 0.04, 8), gold, 0.32, 0.98, 0.0, 0, 0, 0);
    this.put(benchGroup, this.box(0.3, 0.25, 0.35), this.metal(0x475569, 0.5), 0.9, 1.1, 1.7);
    this.put(benchGroup, this.box(0.08, 0.1, 0.5), steel, 0.9, 1.3, 1.7);
    // desk lamp
    this.put(benchGroup, this.cyl(0.14, 0.16, 0.05, 10), dark, 0.2, 0.98, -1.7);
    this.put(benchGroup, this.cyl(0.02, 0.02, 0.7, 6), gold, 0.2, 1.33, -1.7, 0, 0, 0.3);
    this.put(benchGroup, this.cyl(0.04, 0.16, 0.2, 10), this.tactile(0x16a34a), 0.35, 1.68, -1.7, 0, 0, 0.3);
    this.put(benchGroup, this.sph(0.06, 8), this.glow(0xffe08a, 3), 0.37, 1.56, -1.7);
    // parts bins + gears on shelf
    [0, 1, 2].forEach((i) => this.put(benchGroup, this.box(0.3, 0.22, 0.3), this.tactile([0xdc2626, 0x2563eb, 0xfacc15][i]), 1.2, 1.07, -1.1 + i * 0.4));
    // pegboard with tools on the right wall
    const peg = this.put(benchGroup, this.box(0.06, 1.6, 3.2), this.tactile(0xb68355, { roughness: 0.9, knitBump: false }), 2.5, 2.2, 0.0);
    peg.position.x = 2.55;
    for (let i = 0; i < 6; i++) {
      this.put(benchGroup, this.box(0.06, 0.55 + (i % 3) * 0.12, 0.1), i % 2 ? steel : red, 2.46, 2.2, -1.2 + i * 0.5);
      this.put(benchGroup, this.sph(0.09, 8), i % 2 ? gold : steel, 2.46, 2.55, -1.2 + i * 0.5);
    }
    this.put(benchGroup, this.cyl(0.35, 0.35, 0.08, 16), steel, 2.46, 2.9, 0.9, 0, 0, Math.PI / 2);
    const benchLight = new THREE.PointLight(0x7dd3fc, 1.8, 6);
    benchLight.position.set(-0.5, 2.4, 0);
    benchGroup.add(benchLight);
    this.group.add(benchGroup);

    // 8. ZONE G (front centre): SANTA'S SLEIGH + LAUNCH PAD
    const sleighGroup = new THREE.Group();
    sleighGroup.position.copy(this.sleighLaunchPosition);

    const sleighRed = this.tactile(0xd32f2f, { roughness: 0.55 });
    this.put(sleighGroup, this.box(2.6, 0.55, 3.2), sleighRed, 0, 0.5, 1.0);
    this.put(sleighGroup, this.box(2.75, 0.12, 3.35), gold, 0, 0.82, 1.0);
    this.put(sleighGroup, this.box(2.75, 0.1, 3.35), gold, 0, 0.3, 1.0);
    // high back + sides
    this.put(sleighGroup, this.box(2.6, 1.1, 0.2), sleighRed, 0, 1.2, 2.5);
    this.put(sleighGroup, this.box(0.2, 0.5, 3.2), sleighRed, -1.3, 1.0, 1.0);
    this.put(sleighGroup, this.box(0.2, 0.5, 3.2), sleighRed, 1.3, 1.0, 1.0);
    this.put(sleighGroup, this.box(2.9, 0.12, 0.3), gold, 0, 1.8, 2.5);
    // fur-lined seat + cushion
    this.put(sleighGroup, this.box(2.2, 0.2, 1.3), cream, 0, 0.85, 1.7);
    this.put(sleighGroup, this.box(2.1, 0.7, 0.2), this.tactile(0x7f1d1d), 0, 1.3, 2.35);
    // curled front
    this.put(sleighGroup, new THREE.TorusGeometry(0.45, 0.07, 8, 14, Math.PI * 1.2), gold, 0, 0.85, -0.55, 0, Math.PI / 2, 0.4);
    this.put(sleighGroup, this.sph(0.12, 8), this.glow(0xfde047, 2.6), 0, 1.15, -0.62);
    // lanterns
    [-1.3, 1.3].forEach((rx) => {
      this.put(sleighGroup, this.cyl(0.03, 0.03, 0.5, 6), gold, rx, 1.45, -0.4);
      this.put(sleighGroup, this.sph(0.12, 8), this.glow(0xffd27a, 3), rx, 1.8, -0.4);
    });
    // curved runners (arcs) with gold finish
    [-1.2, 1.2].forEach((rx) => {
      this.put(sleighGroup, this.box(0.12, 0.12, 3.8), gold, rx, 0.06, 1.0);
      const curl = this.put(sleighGroup, new THREE.TorusGeometry(0.3, 0.06, 6, 12, Math.PI * 1.1), gold, rx, 0.3, -0.95, 0, Math.PI / 2, -0.9);
      curl.scale.set(1, 1, 1);
      this.put(sleighGroup, this.box(0.08, 0.3, 0.08), gold, rx, 0.2, 0.3);
      this.put(sleighGroup, this.box(0.08, 0.3, 0.08), gold, rx, 0.2, 1.9);
    });
    // gift sack + loaded gifts (decor)
    this.put(sleighGroup, this.sph(0.55, 12), this.tactile(0x7c4a1e), 0.5, 1.2, 1.8).scale.set(1, 1.1, 1);
    this.put(sleighGroup, this.cyl(0.12, 0.2, 0.2, 8), this.tactile(0x5a3418), 0.5, 1.8, 1.8);
    this.addGiftBox(sleighGroup, -0.6, 0.9, 1.9, 0.5, 0x2563eb, 0xfacc15, 0.2);
    this.addGiftBox(sleighGroup, -0.2, 0.9, 1.2, 0.4, 0x16a34a, 0xf8fafc, -0.4);
    // snowdrift beside sleigh
    this.put(sleighGroup, this.sph(0.9, 10), this.tactile(0xf4f8ff, { roughness: 0.95, knitBump: false }), -2.2, 0.1, 1.8).scale.set(1.2, 0.35, 1);

    // launch ring (unchanged behaviour)
    const ringGeo = new THREE.RingGeometry(1.5, 1.85, 32);
    const ringMat = new THREE.MeshBasicMaterial({ color: 0x10b981, side: THREE.DoubleSide, transparent: true, opacity: 0.85 });
    this.launchRingMesh = new THREE.Mesh(ringGeo, ringMat);
    this.launchRingMesh.rotation.x = -Math.PI / 2;
    this.launchRingMesh.position.set(0, 0.06, -0.5);
    sleighGroup.add(this.launchRingMesh);
    const padGlow = new THREE.Mesh(new THREE.CircleGeometry(1.5, 32), new THREE.MeshBasicMaterial({ color: 0x10b981, transparent: true, opacity: 0.12 }));
    padGlow.rotation.x = -Math.PI / 2;
    padGlow.position.set(0, 0.05, -0.5);
    sleighGroup.add(padGlow);

    const sleighLight = new THREE.PointLight(0x34d399, 2.2, 7);
    sleighLight.position.set(0, 2.2, 0);
    sleighGroup.add(sleighLight);
    this.group.add(sleighGroup);

    // 9. DECOR: trees, toy shelves, fireplace, gift piles (no colliders, kept against walls/corners)
    this.addTree(-6.6, 6.4);
    this.addTree(6.6, 6.4, 2.8);
    this.addToyShelf(-7.45, 1.7, Math.PI / 2, 3.0);
    this.addToyShelf(7.45, 1.8, -Math.PI / 2, 3.0);

    // fireplace on back wall, left
    const fp = new THREE.Group();
    fp.position.set(-5.6 + 0.0, 0, -7.4);
    const stone = this.tactile(0x8b7d72, { roughness: 0.95 });
    this.put(fp, this.box(2.4, 0.9, 0.7), stone, 0, 0.45, 0);
    this.put(fp, this.box(0.5, 2.0, 0.7), stone, -0.95, 1.9, 0);
    this.put(fp, this.box(0.5, 2.0, 0.7), stone, 0.95, 1.9, 0);
    this.put(fp, this.box(2.4, 0.4, 0.8), walnut, 0, 3.0, 0);
    this.put(fp, this.box(1.4, 1.6, 0.2), dark, 0, 1.7, -0.2);
    this.put(fp, this.box(1.4, 0.15, 0.55), dark, 0, 0.98, 0.05);
    const flameCols = [0xff6a00, 0xffa31a, 0xffd24a];
    flameCols.forEach((c, i) => this.put(fp, new THREE.ConeGeometry(0.28 - i * 0.07, 0.8 - i * 0.18, 8), this.glow(c, 2.4), (i - 1) * 0.18, 1.45 + i * 0.03, 0.05));
    this.put(fp, this.cyl(0.07, 0.07, 1.1, 6), this.tactile(0x4a2610), -0.2, 1.12, 0.1, 0, 0, Math.PI / 2);
    this.put(fp, this.cyl(0.07, 0.07, 1.1, 6), this.tactile(0x5b3014), 0.2, 1.2, 0.15, 0.3, 0, Math.PI / 2);
    // stockings
    [0x16a34a, 0xdc2626, 0x2563eb].forEach((c, i) => {
      this.put(fp, this.box(0.18, 0.5, 0.1), this.tactile(c), -0.8 + i * 0.8, 2.7, 0.45);
      this.put(fp, this.box(0.3, 0.14, 0.12), cream, -0.8 + i * 0.8, 2.97, 0.45);
    });
    this.group.add(fp);

    // gift piles in back corners
    this.addGiftBox(this.group, -7.0, 0, -6.5, 0.8, 0xdc2626, 0xfacc15, 0.3);
    this.addGiftBox(this.group, -6.2, 0, -6.9, 0.6, 0x2563eb, 0xf8fafc, -0.2);
    this.addGiftBox(this.group, -6.9, 0.8, -6.5, 0.45, 0x16a34a, 0xdc2626, 0.5);
    this.addGiftBox(this.group, 7.0, 0, -6.5, 0.85, 0x7c3aed, 0xfacc15, -0.3);
    this.addGiftBox(this.group, 6.2, 0, -6.9, 0.55, 0xf59e0b, 0xdc2626, 0.2);
    this.addGiftBox(this.group, 6.9, 0.85, -6.5, 0.45, 0x0ea5e9, 0xf8fafc, 0.1);

    // snowy clock on the back wall above board
    const clock = this.put(this.group, this.cyl(0.4, 0.4, 0.1, 20), cream, 0, 3.6, -7.7, Math.PI / 2, 0, 0);
    clock.name = 'wall-clock';
    this.put(this.group, new THREE.TorusGeometry(0.42, 0.05, 8, 20), gold, 0, 3.6, -7.65);
    this.put(this.group, this.box(0.04, 0.28, 0.03), dark, 0, 3.7, -7.62);
    this.put(this.group, this.box(0.2, 0.04, 0.03), dark, 0.08, 3.6, -7.62);
  }

  public update(delta: number, gripLevel: number = 0, sackGiftsCount: number = 0): {
    loadedInSleighCount: number;
    totalOrderGifts: number;
    readyToLaunch: boolean;
  } {
    let loadedOnPad = 0;
    const padCenter = new THREE.Vector3(this.sleighLaunchPosition.x, 0, this.sleighLaunchPosition.z - 0.5);

    for (const gift of this.gifts) {
      if (gift.isInSack) {
        gift.isLoadedInSleigh = true;
        continue;
      }
      gift.update(delta, gripLevel);

      const dist = Math.hypot(gift.mesh.position.x - padCenter.x, gift.mesh.position.z - padCenter.z);
      if (dist < 2.2) {
        gift.isLoadedInSleigh = true;
        loadedOnPad++;
      } else {
        gift.isLoadedInSleigh = false;
      }
    }

    const totalLoaded = loadedOnPad + sackGiftsCount;
    const totalOrderGifts = this.activeOrder.requiredGifts.length;
    const readyToLaunch = totalLoaded >= totalOrderGifts;

    if (this.launchRingMesh) {
      const mat = this.launchRingMesh.material as THREE.MeshBasicMaterial;
      mat.color.setHex(readyToLaunch ? 0x10b981 : 0xfbbf24);
    }

    return {
      loadedInSleighCount: totalLoaded,
      totalOrderGifts,
      readyToLaunch
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

  public getNearbyStation(elfPos: THREE.Vector3): 'ORDER_BOARD' | 'WORKBENCH' | 'SLEIGH' | null {
    if (elfPos.distanceTo(this.orderBoardPosition) < 2.6) return 'ORDER_BOARD';
    if (elfPos.distanceTo(this.workbenchPosition) < 2.6) return 'WORKBENCH';
    if (elfPos.distanceTo(new THREE.Vector3(0, 0, 3.8)) < 2.5) return 'SLEIGH';
    return null;
  }

  public setVisible(visible: boolean) {
    this.group.visible = visible;
    for (const c of this.colliders) {
      c.setEnabled(visible);
    }
    for (const g of this.gifts) {
      g.setVisible(visible);
    }
  }
}
