import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';

const loader = new GLTFLoader();

/**
 * Загружает внешнюю 3D-модель (.glb / .gltf) из папки public/models/,
 * автоматически масштабирует её под нужный размер в метрах и применяет
 * мультяшный Toon-шейдер + черную обводку в стиле игры PEAK.
 */
export async function loadPeakModel(
  url: string,
  targetHeightMeters: number = 1.15
): Promise<THREE.Group | null> {
  return new Promise((resolve) => {
    loader.load(
      url,
      (gltf) => {
        const root = gltf.scene;

        // 1. Вычисляем габариты (BoundingBox) и нормализуем высоту
        const box = new THREE.Box3().setFromObject(root);
        const size = new THREE.Vector3();
        box.getSize(size);
        const center = new THREE.Vector3();
        box.getCenter(center);

        if (size.y > 0.001) {
          const scale = targetHeightMeters / size.y;
          root.scale.setScalar(scale);

          // Пересчитываем бокс после скейла, чтобы поставить ноги ровно в Y = 0
          box.setFromObject(root);
          box.getCenter(center);
          root.position.x = -center.x;
          root.position.y = -box.min.y;
          root.position.z = -center.z;
        }

        // 2. Переводим стандартные материалы в мультяшный Toon-шейдер PEAK
        root.traverse((child) => {
          if ((child as THREE.Mesh).isMesh) {
            const mesh = child as THREE.Mesh;
            mesh.castShadow = true;
            mesh.receiveShadow = true;

            const oldMat = mesh.material as THREE.MeshStandardMaterial;
            if (oldMat && oldMat.color) {
              mesh.material = createPeakMaterial(oldMat.color.getHex(), {
                map: oldMat.map || undefined
              });
            }
          }
        });

        // 3. Накладываем жирную черную обводку
        applyCartoonOutline(root, 0.028);

        const wrapper = new THREE.Group();
        wrapper.add(root);
        resolve(wrapper);
      },
      undefined,
      () => {
        // Если файла еще нет в public/models/ — тихо возвращаем null (используется процедурная PEAK-модель)
        resolve(null);
      }
    );
  });
}

// ---------------------------------------------------------------------------
// Пропсы: Tripo GLB (public/models/props/*.glb) и Kenney FBX (public/models/props/k_*.fbx)
// ---------------------------------------------------------------------------
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const fbxLoader = new FBXLoader();
const propCache = new Map<string, Promise<THREE.Object3D | null>>();

function loadRaw(url: string): Promise<THREE.Object3D | null> {
  let p = propCache.get(url);
  if (!p) {
    p = new Promise((resolve) => {
      if (url.endsWith('.fbx')) {
        fbxLoader.load(url, (obj) => resolve(obj), undefined, () => resolve(null));
      } else {
        loader.load(url, (g) => resolve(g.scene), undefined, () => resolve(null));
      }
    });
    propCache.set(url, p);
  }
  return p;
}

export interface PropOptions {
  /** Целевая высота в метрах. */
  height: number;
  /** Поворот вокруг Y (рад). */
  rotY?: number;
  /** Толстая обводка в стиле игры (по умолчанию да). */
  outline?: boolean;
  /** Переводить материалы в игровой «пластилин» (для Kenney FBX да, для текстурных Tripo — нет). */
  restyle?: boolean;
}

/** Загружает проп, нормализует высоту, ставит ноги в Y=0 и центрирует по XZ. Возвращает null, если файла нет. */
export async function loadProp(url: string, opt: PropOptions): Promise<THREE.Group | null> {
  const src = await loadRaw(url);
  if (!src) return null;
  const root = src.clone(true);
  const box = new THREE.Box3().setFromObject(root);
  const size = new THREE.Vector3();
  box.getSize(size);
  if (size.y < 0.0001) return null;
  root.scale.multiplyScalar(opt.height / size.y);
  box.setFromObject(root);
  const c = new THREE.Vector3();
  box.getCenter(c);
  root.position.set(-c.x, -box.min.y, -c.z);

  root.traverse((o) => {
    const m = o as THREE.Mesh;
    if (!m.isMesh) return;
    m.castShadow = true;
    m.receiveShadow = true;
    if (opt.restyle) {
      const conv = (mat: THREE.Material) => {
        const sm = mat as THREE.MeshStandardMaterial;
        return createPeakMaterial(sm.color ? sm.color.getHex() : 0xcccccc, { map: sm.map || undefined });
      };
      m.material = Array.isArray(m.material) ? m.material.map(conv) : conv(m.material);
    }
  });
  if (opt.outline) applyCartoonOutline(root, 0.02);

  const wrapper = new THREE.Group();
  wrapper.add(root);
  wrapper.rotation.y = opt.rotY ?? 0;
  return wrapper;
}

/** Размер габарита (X,Z) готового пропса — удобно для коллайдеров. */
export function propFootprint(g: THREE.Object3D): THREE.Vector3 {
  return new THREE.Box3().setFromObject(g).getSize(new THREE.Vector3());
}
