import * as THREE from 'three';
import { createTactileMaterial } from './TactileMaterials';

/**
 * Создает тактильный PBR-материал (войлок / вязаная шерсть / теплое дерево с бархатистым микро-бликом sheen)
 * в стилистике Stop-Motion диорам с референсов.
 */
export function createPeakMaterial(
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
  return createTactileMaterial(color, options);
}

/**
 * Включает мягкие тени (castShadow / receiveShadow) на всех вложенных мешах объекта
 * (старая черная плоская обводка убрана, чтобы не портить объемный PBR-рендер).
 */
export function applyCartoonOutline(
  object: THREE.Object3D,
  _thickness: number = 0.0,
  _outlineColor: number = 0x000000
) {
  object.traverse((child) => {
    if ((child as THREE.Mesh).isMesh) {
      child.castShadow = true;
      child.receiveShadow = true;
    }
  });
}
