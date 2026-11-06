import * as THREE from 'three';
import { SoundEngine } from '../audio/SoundEngine';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';
import { loadPeakModel } from '../loaders/ModelLoader';


const geoCache = new Map<string, THREE.BufferGeometry>();
function getGeo(key: string, make: () => THREE.BufferGeometry): THREE.BufferGeometry {
  let g = geoCache.get(key);
  if (!g) {
    g = make();
    geoCache.set(key, g);
  }
  return g;
}
const sphereGeo = (r: number) => getGeo(`s${r}`, () => new THREE.SphereGeometry(r, 24, 18));
const torusGeo = (r: number, t: number) => getGeo(`t${r}_${t}`, () => new THREE.TorusGeometry(r, t, 10, 32));
const capsuleGeo = (r: number, l: number) => getGeo(`c${r}_${l}`, () => new THREE.CapsuleGeometry(r, l, 4, 8));
const coneGeo = (r: number, h: number) =>
  getGeo(`k${r}_${h}`, () => {
    const pts = [new THREE.Vector2(0.001, h / 2), new THREE.Vector2(r * 0.3, h * 0.15), new THREE.Vector2(r * 0.8, -h * 0.3), new THREE.Vector2(r, -h / 2)];
    return new THREE.LatheGeometry(new THREE.SplineCurve(pts).getPoints(12), 14);
  });

const matCache = new Map<string, THREE.MeshPhysicalMaterial>();
function getMat(
  c: number,
  o?: { emissive?: number; emissiveIntensity?: number; roughness?: number; metalness?: number; knitBump?: boolean }
): THREE.MeshPhysicalMaterial {
  const key = `${c}_${o?.emissive ?? ''}_${o?.emissiveIntensity ?? ''}_${o?.roughness ?? ''}_${o?.metalness ?? ''}_${o?.knitBump ?? ''}`;
  let m = matCache.get(key);
  if (!m) {
    m = createPeakMaterial(c, o);
    matCache.set(key, m);
  }
  return m;
}

export class PetCat {
  public mesh: THREE.Group;
  public state: 'SLEEPING' | 'INVESTIGATING' | 'RETURNING' = 'SLEEPING';

  private soundEngine: SoundEngine;
  private sleepPos: THREE.Vector3;
  private tail: THREE.Mesh;
  private stateTimer: number = 0;
  private meowCooldown: number = 0;

  constructor(scene: THREE.Scene, soundEngine: SoundEngine, sleepPos: THREE.Vector3) {
    this.soundEngine = soundEngine;
    this.sleepPos = sleepPos.clone();
    this.mesh = new THREE.Group();

    const { catGroup, tail } = this.buildCatModel();
    this.mesh = catGroup;
    this.tail = tail;

    applyCartoonOutline(this.mesh, 0.032);

    loadPeakModel('/models/cat.glb', 0.45).then((custom) => {
      if (custom) {
        this.mesh.children.forEach((c) => (c.visible = false));
        this.mesh.add(custom);
      }
    });

    this.mesh.position.copy(this.sleepPos);
    scene.add(this.mesh);
  }

  private buildCatModel() {
    const catGroup = new THREE.Group();

    const orange = getMat(0xf08a3c);
    const stripe = getMat(0xc4581c);
    const cream = getMat(0xfff1dc);
    const pink = getMat(0xf4a0a8, { knitBump: false });
    const eyeMat = getMat(0x9be15d, { knitBump: false, roughness: 0.3, emissive: 0x2a4a10, emissiveIntensity: 0.25 });
    const pupilMat = getMat(0x14120f, { knitBump: false, roughness: 0.3 });
    const whiskerMat = getMat(0xffffff, { knitBump: false });
    const collarMat = getMat(0xc23030);
    const bellMat = getMat(0xfacc15, { metalness: 0.8, roughness: 0.2, knitBump: false });

    const add = (geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number, sx = 1, sy = 1, sz = 1) => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.scale.set(sx, sy, sz);
      catGroup.add(m);
      return m;
    };

    // Пухлое круглое тельце
    add(sphereGeo(0.24), orange, 0, 0.22, 0, 1.4, 1.0, 1.15);
    // Полоски на спинке
    [-0.12, 0, 0.12].forEach((x) => {
      const st = add(capsuleGeo(0.018, 0.1), stripe, x, 0.44, 0);
      st.rotation.x = Math.PI / 2;
    });
    // Белое пузико
    add(sphereGeo(0.15), cream, 0.12, 0.13, 0, 1.2, 0.8, 1.0);
    // Лапки
    [[0.22, 0.06], [0.22, -0.06], [-0.2, 0.14], [-0.2, -0.14]].forEach(([x, z]) => {
      add(sphereGeo(0.06), cream, x, 0.04, z * 1.0, 1.3, 0.7, 1.0);
    });

    // Голова (щекастая)
    add(sphereGeo(0.19), orange, 0.34, 0.36, 0, 1.0, 0.92, 1.12);
    // Щёчки-мордочка
    add(sphereGeo(0.09), cream, 0.45, 0.31, 0.05, 1, 0.8, 1);
    add(sphereGeo(0.09), cream, 0.45, 0.31, -0.05, 1, 0.8, 1);

    // Ушки (внешние + розовые внутренние)
    [1, -1].forEach((sd) => {
      const ear = add(coneGeo(0.07, 0.15), orange, 0.33, 0.52, sd * 0.11);
      ear.rotation.x = sd * -0.3;
      const inner = add(coneGeo(0.04, 0.09), pink, 0.345, 0.51, sd * 0.105);
      inner.rotation.x = sd * -0.3;
    });

    // Глаза
    [1, -1].forEach((sd) => {
      add(sphereGeo(0.04), eyeMat, 0.495, 0.385, sd * 0.075, 0.6, 1, 0.9);
      add(sphereGeo(0.02), pupilMat, 0.512, 0.385, sd * 0.075, 0.5, 1.5, 0.6);
    });
    // Носик
    add(sphereGeo(0.022), pink, 0.545, 0.335, 0, 0.8, 0.7, 1.1);
    // Усы
    [1, -1].forEach((sd) => {
      [-0.025, 0, 0.025].forEach((dy, i) => {
        const w = add(capsuleGeo(0.003, 0.15), whiskerMat, 0.52, 0.32 + dy, sd * 0.1);
        w.rotation.x = Math.PI / 2;
        w.rotation.y = sd * (0.25 + i * 0.0);
        w.rotation.z = (i - 1) * 0.12;
      });
    });

    // Красный вязаный ошейник с колокольчиком
    const collar = add(torusGeo(0.17, 0.03), collarMat, 0.27, 0.3, 0);
    collar.rotation.y = Math.PI / 2;
    collar.rotation.x = 0;
    add(sphereGeo(0.038), bellMat, 0.37, 0.18, 0);

    // Хвост: изогнутая труба (пивот у основания, анимация rotation.x сохраняется)
    const tailGeo = getGeo('cat_tail', () => {
      const curve = new THREE.CatmullRomCurve3([
        new THREE.Vector3(0, 0, 0),
        new THREE.Vector3(-0.1, 0.12, 0),
        new THREE.Vector3(-0.14, 0.3, 0),
        new THREE.Vector3(-0.06, 0.42, 0)
      ]);
      return new THREE.TubeGeometry(curve, 16, 0.04, 10, false);
    });
    const tail = new THREE.Mesh(tailGeo, orange);
    tail.position.set(-0.32, 0.2, 0);
    tail.rotation.z = 0.2;
    catGroup.add(tail);
    const tailTip = new THREE.Mesh(sphereGeo(0.045), cream);
    tailTip.position.set(-0.06, 0.42, 0);
    tail.add(tailTip);

    return { catGroup, tail };
  }

  public setVisible(visible: boolean) {
    this.mesh.visible = visible;
  }

  public update(delta: number, currentNoise: number, noiseSourcePos?: THREE.Vector3): { noiseAdded: number } {
    let noiseAdded = 0;
    this.stateTimer += delta;
    this.meowCooldown -= delta;

    // Покачивание хвостика
    this.tail.rotation.x = Math.sin(this.stateTimer * 3) * 0.3;

    if (this.state === 'SLEEPING') {
      // Кот дышит
      this.mesh.scale.y = 1.0 + Math.sin(this.stateTimer * 2) * 0.05;

      // Если рядом шумно (> 32 dB), кот просыпается!
      if (currentNoise > 32 && noiseSourcePos) {
        const dist = this.mesh.position.distanceTo(noiseSourcePos);
        if (dist < 6.0) {
          this.state = 'INVESTIGATING';
          this.stateTimer = 0;
          if (this.meowCooldown <= 0) {
            this.soundEngine.playCatMeow();
            this.meowCooldown = 5.0;
            noiseAdded = 25;
          }
        }
      }
    } else if (this.state === 'INVESTIGATING') {
      // Идет на цыпочках в сторону шума
      if (noiseSourcePos) {
        const dir = new THREE.Vector3(
          noiseSourcePos.x - this.mesh.position.x,
          0,
          noiseSourcePos.z - this.mesh.position.z
        );
        if (dir.length() > 1.2) {
          dir.normalize();
          this.mesh.position.addScaledVector(dir, delta * 1.0);
          this.mesh.rotation.y = Math.atan2(dir.x, dir.z);
        }
      }

      if (this.stateTimer > 7.0) {
        this.state = 'RETURNING';
        this.stateTimer = 0;
      }
    } else if (this.state === 'RETURNING') {
      const dir = new THREE.Vector3(
        this.sleepPos.x - this.mesh.position.x,
        0,
        this.sleepPos.z - this.mesh.position.z
      );
      if (dir.length() > 0.3) {
        dir.normalize();
        this.mesh.position.addScaledVector(dir, delta * 1.0);
        this.mesh.rotation.y = Math.atan2(dir.x, dir.z);
      } else {
        this.state = 'SLEEPING';
        this.stateTimer = 0;
      }
    }

    return { noiseAdded };
  }
}
