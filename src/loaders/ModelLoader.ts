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
