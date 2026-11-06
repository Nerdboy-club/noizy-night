import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import {
  buildApprovedHeistElf,
  buildApprovedGrandpa,
  buildApprovedCat,
  BuiltCharacterAsset
} from './ProceduralRigs';

export interface ModelStats {
  name: string;
  width: number;
  height: number;
  depth: number;
  triangles: number;
  vertices: number;
  meshes: number;
  materials: number;
  bones: number;
  hasHumanoidRig: boolean;
}

export class ModelInspector {
  public currentAsset: BuiltCharacterAsset | null = null;
  public stats: ModelStats = {
    name: 'None',
    width: 0,
    height: 0,
    depth: 0,
    triangles: 0,
    vertices: 0,
    meshes: 0,
    materials: 0,
    bones: 0,
    hasHumanoidRig: false
  };

  // Visual Helpers
  public bboxHelper: THREE.Box3Helper;
  public originAxes: THREE.AxesHelper;
  public skeletonHelper: THREE.SkeletonHelper | null = null;
  public currentBBox: THREE.Box3 = new THREE.Box3();

  public showBBox: boolean = false;
  public showOriginAxes: boolean = false;
  public showSkeleton: boolean = false;
  public showWireframe: boolean = false;

  // Animation Inspector State
  public mixer: THREE.AnimationMixer | null = null;
  public activeAction: THREE.AnimationAction | null = null;
  public activeClip: THREE.AnimationClip | null = null;
  public isPlayingAnim: boolean = false;
  public isPausedAnim: boolean = false;
  public isBindPose: boolean = false;
  public animSpeed: number = 1.0;

  private scene: THREE.Scene;
  private loader: GLTFLoader;
  private initialBoneTransforms: Map<THREE.Bone, { pos: THREE.Vector3; quat: THREE.Quaternion }> = new Map();

  constructor(scene: THREE.Scene) {
    this.scene = scene;
    this.loader = new GLTFLoader();

    this.bboxHelper = new THREE.Box3Helper(this.currentBBox, new THREE.Color(0x38bdf8));
    this.bboxHelper.visible = false;
    this.scene.add(this.bboxHelper);

    this.originAxes = new THREE.AxesHelper(0.65);
    this.originAxes.visible = false;
    this.scene.add(this.originAxes);
  }

  public loadPreset(presetId: 'elf' | 'grandpa' | 'cat'): BuiltCharacterAsset {
    this.clearCurrentModel();

    let asset: BuiltCharacterAsset;
    if (presetId === 'elf') {
      asset = buildApprovedHeistElf();
    } else if (presetId === 'grandpa') {
      asset = buildApprovedGrandpa();
    } else {
      asset = buildApprovedCat();
    }

    this.setupLoadedAsset(asset, presetId === 'elf' ? 'Heist Elf (Approved)' : presetId === 'grandpa' ? 'Grumpy Grandpa (Approved)' : 'Chubby Cat (Approved)');
    return asset;
  }

  public async loadFromFile(file: File): Promise<BuiltCharacterAsset> {
    const objectUrl = URL.createObjectURL(file);
    try {
      const asset = await this.loadFromUrl(objectUrl, file.name);
      return asset;
    } finally {
      URL.revokeObjectURL(objectUrl);
    }
  }

  public async loadFromUrl(url: string, displayName?: string): Promise<BuiltCharacterAsset> {
    return new Promise((resolve, reject) => {
      this.loader.load(
        url,
        (gltf) => {
          this.clearCurrentModel();

          const wrapper = new THREE.Group();
          const modelRoot = gltf.scene;

          // Автоцентрирование и выравнивание подошвы по Y = 0
          const box = new THREE.Box3().setFromObject(modelRoot);
          const size = new THREE.Vector3();
          const center = new THREE.Vector3();
          box.getSize(size);
          box.getCenter(center);

          // Если модель гигантская (например в сантиметрах из Mixamo/Blender) — нормализуем к ~1.25м
          if (size.y > 4.0 || size.y < 0.15) {
            const scale = 1.2 / Math.max(0.001, size.y);
            modelRoot.scale.setScalar(scale);
            box.setFromObject(modelRoot);
            box.getCenter(center);
          }

          modelRoot.position.x = -center.x;
          modelRoot.position.y = -box.min.y;
          modelRoot.position.z = -center.z;
          wrapper.add(modelRoot);

          // Собираем кости, если есть
          const bonesList: THREE.Bone[] = [];
          const bonesMap: Record<string, THREE.Bone> = {};
          modelRoot.traverse((child) => {
            if ((child as THREE.Mesh).isMesh) {
              child.castShadow = true;
              child.receiveShadow = true;
            }
            if ((child as THREE.Bone).isBone) {
              const b = child as THREE.Bone;
              bonesList.push(b);
              bonesMap[b.name] = b;
            }
          });

          const asset: BuiltCharacterAsset = {
            root: wrapper,
            skeleton: new THREE.Skeleton(bonesList),
            bones: bonesMap,
            limbSpecs: [],
            animations: gltf.animations || [],
            hasHumanoidRig: bonesList.length >= 6
          };

          this.setupLoadedAsset(asset, displayName || url.split('/').pop() || 'Imported GLB');
          resolve(asset);
        },
        undefined,
        (err) => reject(err)
      );
    });
  }

  private clearCurrentModel() {
    if (this.activeAction) {
      this.activeAction.stop();
      this.activeAction = null;
    }
    this.mixer = null;
    this.activeClip = null;
    this.isPlayingAnim = false;
    this.isPausedAnim = false;

    if (this.skeletonHelper) {
      this.scene.remove(this.skeletonHelper);
      this.skeletonHelper = null;
    }

    if (this.currentAsset) {
      this.scene.remove(this.currentAsset.root);
      this.currentAsset = null;
    }
    this.initialBoneTransforms.clear();
  }

  private setupLoadedAsset(asset: BuiltCharacterAsset, name: string) {
    this.currentAsset = asset;
    this.scene.add(asset.root);
    asset.root.updateMatrixWorld(true);

    // Сохраняем Bind Pose всех костей
    asset.root.traverse((obj) => {
      if ((obj as THREE.Bone).isBone) {
        const b = obj as THREE.Bone;
        this.initialBoneTransforms.set(b, {
          pos: b.position.clone(),
          quat: b.quaternion.clone()
        });
      }
    });

    // SkeletonHelper
    if (Object.keys(asset.bones).length > 0) {
      this.skeletonHelper = new THREE.SkeletonHelper(asset.root);
      this.skeletonHelper.visible = this.showSkeleton;
      this.scene.add(this.skeletonHelper);
    }

    // AnimationMixer
    this.mixer = new THREE.AnimationMixer(asset.root);
    if (asset.animations.length > 0) {
      this.activeClip = asset.animations[0];
    }

    this.applyWireframe(this.showWireframe);
    this.computeStats(name);
  }

  public computeStats(name?: string) {
    if (!this.currentAsset) return;
    const root = this.currentAsset.root;

    this.currentBBox.setFromObject(root);
    const size = new THREE.Vector3();
    this.currentBBox.getSize(size);

    let triangles = 0;
    let vertices = 0;
    let meshes = 0;
    let bones = 0;
    const materialsSet = new Set<THREE.Material>();

    root.traverse((obj) => {
      if ((obj as THREE.Bone).isBone) {
        bones++;
      }
      if ((obj as THREE.Mesh).isMesh) {
        meshes++;
        const mesh = obj as THREE.Mesh;
        const geom = mesh.geometry;
        if (geom) {
          const posAttr = geom.attributes.position;
          if (posAttr) {
            vertices += posAttr.count;
            if (geom.index) {
              triangles += geom.index.count / 3;
            } else {
              triangles += posAttr.count / 3;
            }
          }
        }
        if (Array.isArray(mesh.material)) {
          mesh.material.forEach((m) => materialsSet.add(m));
        } else if (mesh.material) {
          materialsSet.add(mesh.material);
        }
      }
    });

    this.stats = {
      name: name || this.stats.name,
      width: Number(size.x.toFixed(2)),
      height: Number(size.y.toFixed(2)),
      depth: Number(size.z.toFixed(2)),
      triangles: Math.round(triangles),
      vertices,
      meshes,
      materials: materialsSet.size,
      bones,
      hasHumanoidRig: this.currentAsset.hasHumanoidRig
    };
  }

  public applyWireframe(enabled: boolean) {
    this.showWireframe = enabled;
    if (!this.currentAsset) return;
    this.currentAsset.root.traverse((obj) => {
      if ((obj as THREE.Mesh).isMesh) {
        const m = obj as THREE.Mesh;
        if (Array.isArray(m.material)) {
          m.material.forEach((mat) => ((mat as THREE.MeshStandardMaterial).wireframe = enabled));
        } else if (m.material) {
          (m.material as THREE.MeshStandardMaterial).wireframe = enabled;
        }
      }
    });
  }

  // --- УПРАВЛЕНИЕ АНИМАЦИЯМИ (Раздел 2) ---

  public selectClip(clipName: string) {
    if (!this.currentAsset || !this.mixer) return;
    const found = this.currentAsset.animations.find((c) => c.name === clipName);
    if (!found) return;

    const wasPlaying = this.isPlayingAnim;
    this.stopAnimation();
    this.activeClip = found;
    if (wasPlaying) {
      this.playAnimation();
    }
  }

  public playAnimation() {
    if (!this.mixer || !this.activeClip) return;
    this.isBindPose = false;

    if (this.isPausedAnim && this.activeAction) {
      this.activeAction.paused = false;
      this.isPausedAnim = false;
      this.isPlayingAnim = true;
      return;
    }

    this.mixer.stopAllAction();
    this.activeAction = this.mixer.clipAction(this.activeClip);
    this.activeAction.timeScale = this.animSpeed;
    this.activeAction.play();
    this.isPlayingAnim = true;
    this.isPausedAnim = false;
  }

  public pauseAnimation() {
    if (!this.activeAction || !this.isPlayingAnim) return;
    this.activeAction.paused = !this.activeAction.paused;
    this.isPausedAnim = this.activeAction.paused;
  }

  public stopAnimation() {
    if (this.mixer) {
      this.mixer.stopAllAction();
    }
    this.activeAction = null;
    this.isPlayingAnim = false;
    this.isPausedAnim = false;
    this.resetToBindPose();
  }

  public setAnimationSpeed(speed: number) {
    this.animSpeed = speed;
    if (this.activeAction) {
      this.activeAction.timeScale = speed;
    }
  }

  public scrubTimeline(normalizedProgress: number) {
    if (!this.mixer || !this.activeClip) return;
    if (!this.activeAction) {
      this.activeAction = this.mixer.clipAction(this.activeClip);
      this.activeAction.play();
    }
    this.activeAction.paused = true;
    this.isPausedAnim = true;
    this.isPlayingAnim = true;
    this.activeAction.time = normalizedProgress * this.activeClip.duration;
    this.mixer.update(0);
  }

  public resetToBindPose() {
    this.initialBoneTransforms.forEach((data, bone) => {
      bone.position.copy(data.pos);
      bone.quaternion.copy(data.quat);
      bone.updateMatrixWorld(true);
    });
  }

  public update(delta: number) {
    if (!this.currentAsset) return;

    if (this.isPlayingAnim && !this.isPausedAnim && !this.isBindPose && this.mixer) {
      this.mixer.update(delta);
    }

    // Обновление BBox и Origin Axes
    this.bboxHelper.visible = this.showBBox;
    if (this.showBBox) {
      this.currentBBox.setFromObject(this.currentAsset.root);
    }

    this.originAxes.visible = this.showOriginAxes;
    if (this.showOriginAxes) {
      this.originAxes.position.copy(this.currentAsset.root.position);
    }

    if (this.skeletonHelper) {
      this.skeletonHelper.visible = this.showSkeleton;
    }
  }

  public getAnimationProgress(): { current: number; duration: number; percent: number } {
    if (!this.activeClip || !this.activeAction) {
      return { current: 0, duration: this.activeClip?.duration || 1, percent: 0 };
    }
    const dur = Math.max(0.001, this.activeClip.duration);
    const cur = this.activeAction.time % dur;
    return {
      current: cur,
      duration: dur,
      percent: (cur / dur) * 100
    };
  }
}
