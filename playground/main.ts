import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import RAPIER from '@dimforge/rapier3d-compat';
import { PlaygroundArena } from './PlaygroundArena';
import { ModelInspector } from './ModelInspector';
import { ActiveRagdollSystem } from './ActiveRagdollSystem';
import { MouseManipulator } from './MouseManipulator';
import { DebugVisualizer } from './DebugVisualizer';
import { BuiltCharacterAsset } from './ProceduralRigs';

class CharacterPlaygroundApp {
  private renderer!: THREE.WebGLRenderer;
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private controls!: OrbitControls;

  // Lighting
  private ambientLight!: THREE.HemisphereLight;
  private keyLight!: THREE.DirectionalLight;
  private fillLight!: THREE.DirectionalLight;
  private rimLight!: THREE.DirectionalLight;
  private lightRigGroup!: THREE.Group;
  private lightIntensity: number = 1.6;
  private lightingPreset: 'studio' | 'outdoor' = 'studio';

  // Systems
  private world!: RAPIER.World;
  private arena!: PlaygroundArena;
  private inspector!: ModelInspector;
  private ragdoll!: ActiveRagdollSystem;
  private manipulator!: MouseManipulator;
  private debugVis!: DebugVisualizer;

  // Timing & State
  private isSimPaused: boolean = false;
  private lastTime: number = 0;
  private fpsFrameCount: number = 0;
  private fpsAccumTime: number = 0;
  private currentFps: number = 60;
  private keys: Record<string, boolean> = {};

  constructor() {
    this.init();
  }

  private async init() {
    await RAPIER.init();

    const container = document.getElementById('viewport-container')!;
    const canvas = document.getElementById('playground-canvas') as HTMLCanvasElement;

    // 1. Renderer & Scene
    this.renderer = new THREE.WebGLRenderer({
      canvas,
      antialias: true,
      powerPreference: 'high-performance'
    });
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.08;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x111318);

    const pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmremGenerator.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environmentIntensity = 0.55;

    // 2. Camera & OrbitControls
    this.camera = new THREE.PerspectiveCamera(
      48,
      container.clientWidth / container.clientHeight,
      0.05,
      120
    );
    this.camera.position.set(0, 1.35, 2.85);

    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(0, 0.58, 0);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.maxDistance = 28;
    this.controls.minDistance = 0.4;
    this.controls.update();

    // 3. Studio / Outdoor Lighting Rig
    this.setupLighting();

    // 4. Rapier3D World & Arena
    this.world = new RAPIER.World({ x: 0, y: -18.0, z: 0 });
    this.arena = new PlaygroundArena(this.scene, this.world);

    // 5. Core Modules
    this.inspector = new ModelInspector(this.scene);
    this.ragdoll = new ActiveRagdollSystem(this.world);

    this.manipulator = new MouseManipulator(
      this.scene,
      this.camera,
      canvas,
      this.controls,
      this.world,
      this.ragdoll,
      (partName) => {
        const ind = document.getElementById('grab-indicator');
        const nameEl = document.getElementById('grab-part-name');
        if (ind && nameEl) {
          if (partName) {
            ind.style.display = 'flex';
            nameEl.innerText = partName;
          } else {
            ind.style.display = 'none';
          }
        }
      }
    );

    this.debugVis = new DebugVisualizer(this.scene, this.world, this.ragdoll, this.manipulator);
    this.debugVis.showCenterOfMass = true;
    this.debugVis.showForceVectors = true;

    // 6. Load Default Approved Heist Elf Model
    const defaultAsset = this.inspector.loadPreset('elf');
    this.activateCharacterAsset(defaultAsset);

    // 7. UI & Event Listeners
    this.bindUI();
    this.bindKeyboardAndResize(container);

    // 8. Main Loop
    this.lastTime = performance.now();
    requestAnimationFrame((t) => this.loop(t));
  }

  private setupLighting() {
    this.ambientLight = new THREE.HemisphereLight(0xe2e8f0, 0x1e293b, 0.75);
    this.scene.add(this.ambientLight);

    this.lightRigGroup = new THREE.Group();
    this.lightRigGroup.rotation.y = THREE.MathUtils.degToRad(45);

    this.keyLight = new THREE.DirectionalLight(0xfff7ed, 1.6);
    this.keyLight.position.set(4, 8, 5);
    this.keyLight.castShadow = true;
    this.keyLight.shadow.mapSize.set(2048, 2048);
    this.keyLight.shadow.camera.near = 0.5;
    this.keyLight.shadow.camera.far = 25;
    const d = 8;
    this.keyLight.shadow.camera.left = -d;
    this.keyLight.shadow.camera.right = d;
    this.keyLight.shadow.camera.top = d;
    this.keyLight.shadow.camera.bottom = -d;
    this.lightRigGroup.add(this.keyLight);

    this.fillLight = new THREE.DirectionalLight(0x93c5fd, 0.65);
    this.fillLight.position.set(-5, 4, 2);
    this.lightRigGroup.add(this.fillLight);

    this.rimLight = new THREE.DirectionalLight(0xfde047, 0.55);
    this.rimLight.position.set(0, 5, -6);
    this.lightRigGroup.add(this.rimLight);

    this.scene.add(this.lightRigGroup);
  }

  private applyLightingPreset(preset: 'studio' | 'outdoor') {
    this.lightingPreset = preset;
    if (preset === 'studio') {
      this.ambientLight.color.setHex(0xe2e8f0);
      this.ambientLight.groundColor.setHex(0x1e293b);
      this.keyLight.color.setHex(0xfff7ed);
      this.fillLight.color.setHex(0x93c5fd);
      this.rimLight.color.setHex(0xfde047);
    } else {
      // Outdoor winter twilight / sunlight
      this.ambientLight.color.setHex(0x7dd3fc);
      this.ambientLight.groundColor.setHex(0x334155);
      this.keyLight.color.setHex(0xfef08a);
      this.fillLight.color.setHex(0x38bdf8);
      this.rimLight.color.setHex(0xc084fc);
    }
    this.updateLightIntensity(this.lightIntensity);
  }

  private updateLightIntensity(val: number) {
    this.lightIntensity = val;
    this.keyLight.intensity = val;
    this.fillLight.intensity = val * 0.42;
    this.rimLight.intensity = val * 0.35;
    this.ambientLight.intensity = val * 0.45;
  }

  private activateCharacterAsset(asset: BuiltCharacterAsset) {
    // 1. Строим физический Active Ragdoll (или одиночный RigidBody для моделей без рига)
    this.ragdoll.buildFromAsset(asset, new THREE.Vector3(0, 0.02, 0));

    // 2. Обновляем статистику модели в левой панели
    const s = this.inspector.stats;
    this.setText('model-name-label', s.name);
    this.setText('stat-dims', `${s.width} × ${s.height} × ${s.depth} m`);
    this.setText('stat-tris', s.triangles.toLocaleString());
    this.setText('stat-verts', s.vertices.toLocaleString());
    this.setText('stat-meshes', s.meshes.toString());
    this.setText('stat-mats', s.materials.toString());
    this.setText(
      'stat-bones',
      s.bones > 0
        ? `${s.bones} Bones (${s.hasHumanoidRig ? 'Humanoid Rig' : 'Custom Rig'})`
        : '0 Bones (Static Mesh)'
    );

    // 3. Обновляем статус-бейдж Active Ragdoll в верхнем тулбаре
    const rigBadge = document.getElementById('rig-status-badge');
    if (rigBadge) {
      if (this.ragdoll.isFullRagdollAvailable) {
        rigBadge.className = 'rig-badge';
        rigBadge.innerText = `Active Ragdoll: ${this.ragdoll.limbs.size} Bodies / ${this.ragdoll.joints.length} Joints`;
      } else {
        rigBadge.className = 'rig-badge warn';
        rigBadge.innerText = 'Static Mesh Fallback (No Humanoid Rig — Single RigidBody)';
      }
    }

    // 4. Заполняем список анимаций в Animation Inspector
    const clipSelect = document.getElementById('anim-clip-select') as HTMLSelectElement | null;
    const countBadge = document.getElementById('anim-count-badge');
    if (clipSelect) {
      clipSelect.innerHTML = '';
      if (asset.animations.length === 0) {
        const opt = document.createElement('option');
        opt.value = '';
        opt.innerText = 'No Animation Clips in Model';
        clipSelect.appendChild(opt);
      } else {
        asset.animations.forEach((clip) => {
          const opt = document.createElement('option');
          opt.value = clip.name;
          opt.innerText = `${clip.name} (${clip.duration.toFixed(2)}s)`;
          clipSelect.appendChild(opt);
        });
      }
    }
    if (countBadge) {
      countBadge.innerText = `${asset.animations.length} Clips`;
    }

    this.focusCameraOnCharacter('front');
  }

  public focusCameraOnCharacter(preset?: 'front' | 'back' | 'left' | 'right' | 'top') {
    const center = this.ragdoll.centerOfMass.lengthSq() > 0.001
      ? this.ragdoll.centerOfMass.clone()
      : new THREE.Vector3(0, 0.58, 0);

    const dist = Math.max(1.8, this.inspector.stats.height * 2.1);
    this.controls.target.copy(center);

    if (preset === 'front') {
      this.camera.position.set(center.x, center.y + 0.25, center.z + dist);
    } else if (preset === 'back') {
      this.camera.position.set(center.x, center.y + 0.25, center.z - dist);
    } else if (preset === 'left') {
      this.camera.position.set(center.x - dist, center.y + 0.25, center.z);
    } else if (preset === 'right') {
      this.camera.position.set(center.x + dist, center.y + 0.25, center.z);
    } else if (preset === 'top') {
      this.camera.position.set(center.x, center.y + dist * 1.35, center.z + 0.01);
    } else {
      const dir = this.camera.position.clone().sub(this.controls.target).normalize();
      this.camera.position.copy(center).addScaledVector(dir, dist);
    }

    this.controls.update();
  }

  private bindUI() {
    // --- TOP TOOLBAR: CAMERA PRESETS ---
    document.querySelectorAll('[data-cam]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const preset = btn.getAttribute('data-cam') as 'front' | 'back' | 'left' | 'right' | 'top';
        this.focusCameraOnCharacter(preset);
      });
    });

    document.getElementById('btn-focus-cam')?.addEventListener('click', () => {
      this.focusCameraOnCharacter();
    });

    // Lighting buttons
    const btnStudio = document.getElementById('btn-light-studio');
    const btnOutdoor = document.getElementById('btn-light-outdoor');
    btnStudio?.addEventListener('click', () => {
      btnStudio.classList.add('primary');
      btnOutdoor?.classList.remove('primary');
      this.applyLightingPreset('studio');
    });
    btnOutdoor?.addEventListener('click', () => {
      btnOutdoor.classList.add('primary');
      btnStudio?.classList.remove('primary');
      this.applyLightingPreset('outdoor');
    });

    // Simulation Pause / Resume
    const btnSimToggle = document.getElementById('btn-sim-toggle');
    btnSimToggle?.addEventListener('click', () => {
      this.isSimPaused = !this.isSimPaused;
      btnSimToggle.innerText = this.isSimPaused ? '▶ Resume Sim' : '⏸ Pause Sim';
      btnSimToggle.classList.toggle('active', !this.isSimPaused);
    });

    // Reset All
    document.getElementById('btn-reset-all')?.addEventListener('click', () => {
      this.ragdoll.resetToOrigin();
      this.arena.resetProps();
      this.focusCameraOnCharacter('front');
    });

    // --- LEFT PANEL: 1. MODEL LOADER (DRAG & DROP + FILE PICKER + PRESETS) ---
    const dropzone = document.getElementById('dropzone-box');
    const fileInput = document.getElementById('file-input') as HTMLInputElement | null;
    const dropOverlay = document.getElementById('drop-overlay');

    dropzone?.addEventListener('click', () => fileInput?.click());

    fileInput?.addEventListener('change', async () => {
      const file = fileInput.files?.[0];
      if (file) {
        const asset = await this.inspector.loadFromFile(file);
        this.activateCharacterAsset(asset);
      }
    });

    // Global Drag & Drop onto Viewport
    window.addEventListener('dragover', (e) => {
      e.preventDefault();
      if (dropOverlay) dropOverlay.style.display = 'flex';
      dropzone?.classList.add('dragover');
    });
    window.addEventListener('dragleave', (e) => {
      if (e.relatedTarget === null) {
        if (dropOverlay) dropOverlay.style.display = 'none';
        dropzone?.classList.remove('dragover');
      }
    });
    window.addEventListener('drop', async (e) => {
      e.preventDefault();
      if (dropOverlay) dropOverlay.style.display = 'none';
      dropzone?.classList.remove('dragover');

      const file = e.dataTransfer?.files?.[0];
      if (file && (file.name.endsWith('.glb') || file.name.endsWith('.gltf'))) {
        const asset = await this.inspector.loadFromFile(file);
        this.activateCharacterAsset(asset);
      }
    });

    // Preset / Asset Selector
    const presetSelect = document.getElementById('preset-select') as HTMLSelectElement | null;
    presetSelect?.addEventListener('change', async () => {
      const val = presetSelect.value;
      if (val.startsWith('preset:')) {
        const id = val.replace('preset:', '') as 'elf' | 'grandpa' | 'cat';
        const asset = this.inspector.loadPreset(id);
        this.activateCharacterAsset(asset);
      } else if (val.startsWith('url:')) {
        const url = val.replace('url:', '');
        try {
          const asset = await this.inspector.loadFromUrl(url);
          this.activateCharacterAsset(asset);
        } catch {
          alert(`File ${url} not found in public/models/ yet. Drop your .glb file into public/models/ or drag & drop it directly into the viewport!`);
          presetSelect.value = 'preset:elf';
        }
      }
    });

    document.getElementById('btn-center-model')?.addEventListener('click', () => {
      this.ragdoll.resetToOrigin(new THREE.Vector3(0, 0.04, 0));
      this.focusCameraOnCharacter('front');
    });

    document.getElementById('btn-reset-pose')?.addEventListener('click', () => {
      this.inspector.stopAnimation();
      this.ragdoll.resetToOrigin(new THREE.Vector3(0, 0.04, 0));
    });

    // --- LEFT PANEL: 3. VISUAL CONTROLS ---
    this.bindCheckbox('chk-floor', (v) => (this.arena.floorMesh.visible = v));
    this.bindCheckbox('chk-grid', (v) => (this.arena.gridHelper.visible = v));
    this.bindCheckbox('chk-wireframe', (v) => this.inspector.applyWireframe(v));
    this.bindCheckbox('chk-skeleton', (v) => (this.inspector.showSkeleton = v));
    this.bindCheckbox('chk-bbox', (v) => (this.inspector.showBBox = v));
    this.bindCheckbox('chk-axes', (v) => (this.inspector.showOriginAxes = v));

    const bgColorInput = document.getElementById('inp-bg-color') as HTMLInputElement | null;
    bgColorInput?.addEventListener('input', () => {
      this.scene.background = new THREE.Color(bgColorInput.value);
    });

    this.bindSlider('rng-light-int', 'val-light-int', (v) => {
      this.updateLightIntensity(v);
      return v.toFixed(1);
    });

    this.bindSlider('rng-light-rot', 'val-light-rot', (v) => {
      this.lightRigGroup.rotation.y = THREE.MathUtils.degToRad(v);
      return `${Math.round(v)}°`;
    });

    // --- LEFT PANEL: 4. ANIMATION INSPECTOR ---
    const clipSelect = document.getElementById('anim-clip-select') as HTMLSelectElement | null;
    clipSelect?.addEventListener('change', () => {
      this.inspector.selectClip(clipSelect.value);
    });

    document.getElementById('btn-anim-play')?.addEventListener('click', () => {
      this.inspector.playAnimation();
    });
    document.getElementById('btn-anim-pause')?.addEventListener('click', () => {
      this.inspector.pauseAnimation();
    });
    document.getElementById('btn-anim-stop')?.addEventListener('click', () => {
      this.inspector.stopAnimation();
    });
    document.getElementById('btn-bind-pose')?.addEventListener('click', () => {
      this.inspector.isBindPose = true;
      this.inspector.stopAnimation();
    });
    document.getElementById('btn-anim-pose')?.addEventListener('click', () => {
      this.inspector.isBindPose = false;
      this.inspector.playAnimation();
    });

    this.bindSlider('rng-anim-speed', 'val-anim-speed', (v) => {
      this.inspector.setAnimationSpeed(v);
      return `${v.toFixed(1)}x`;
    });

    const scrubInput = document.getElementById('rng-anim-scrub') as HTMLInputElement | null;
    scrubInput?.addEventListener('input', () => {
      const pct = parseFloat(scrubInput.value) / 100;
      this.inspector.scrubTimeline(pct);
    });

    // --- RIGHT PANEL: 1. INTERACTIVE RAGDOLL CONTROLS ---
    const btnModeActive = document.getElementById('btn-mode-active');
    const btnModeRagdoll = document.getElementById('btn-mode-ragdoll');

    btnModeActive?.addEventListener('click', () => {
      this.ragdoll.mode = 'ACTIVE';
      this.ragdoll.standUp();
      btnModeActive.classList.add('active');
      btnModeRagdoll?.classList.remove('active');
    });

    btnModeRagdoll?.addEventListener('click', () => {
      this.ragdoll.mode = 'RAGDOLL';
      btnModeRagdoll.classList.add('active');
      btnModeActive?.classList.remove('active');
    });

    const getCamForward = () => {
      const f = new THREE.Vector3();
      this.camera.getWorldDirection(f);
      return f;
    };

    document.getElementById('btn-act-push')?.addEventListener('click', () => {
      this.ragdoll.pushCharacter(this.manipulator.forceMultiplier, getCamForward());
    });
    document.getElementById('btn-act-knock')?.addEventListener('click', () => {
      this.ragdoll.knockDown(this.manipulator.forceMultiplier);
    });
    document.getElementById('btn-act-throw')?.addEventListener('click', () => {
      this.ragdoll.throwCharacter(this.manipulator.forceMultiplier, getCamForward());
    });
    document.getElementById('btn-act-stand')?.addEventListener('click', () => {
      this.ragdoll.standUp();
      btnModeActive?.classList.add('active');
      btnModeRagdoll?.classList.remove('active');
    });
    document.getElementById('btn-act-reset')?.addEventListener('click', () => {
      this.ragdoll.resetToOrigin();
    });

    this.bindSlider('rng-force-str', 'val-force-str', (v) => {
      this.manipulator.forceMultiplier = v;
      return v.toFixed(1);
    });

    // --- RIGHT PANEL: 2. PHYSICS & BALANCE TUNING ---
    this.bindSlider('rng-gravity', 'val-gravity', (v) => {
      this.world.gravity = { x: 0, y: v, z: 0 };
      return `${v.toFixed(1)} m/s²`;
    });

    this.bindSlider('rng-mass', 'val-mass', (v) => {
      this.ragdoll.updateMaterialProperties(v, this.ragdoll.friction, this.ragdoll.restitution);
      return `${v.toFixed(1)}x`;
    });

    this.bindSlider('rng-friction', 'val-friction', (v) => {
      this.ragdoll.updateMaterialProperties(this.ragdoll.massScale, v, this.ragdoll.restitution);
      this.arena.setFriction(v);
      return v.toFixed(2);
    });

    this.bindSlider('rng-bounce', 'val-bounce', (v) => {
      this.ragdoll.updateMaterialProperties(this.ragdoll.massScale, this.ragdoll.friction, v);
      this.arena.setRestitution(v);
      return v.toFixed(2);
    });

    this.bindSlider('rng-stiffness', 'val-stiffness', (v) => {
      this.ragdoll.jointStiffness = v;
      return v.toFixed(1);
    });

    this.bindSlider('rng-damping', 'val-damping', (v) => {
      this.ragdoll.jointDamping = v;
      return v.toFixed(1);
    });

    this.bindSlider('rng-balance', 'val-balance', (v) => {
      this.ragdoll.balanceStrength = v;
      return v.toFixed(1);
    });

    this.bindSlider('rng-moveforce', 'val-moveforce', (v) => {
      this.ragdoll.movementForce = v;
      return v.toFixed(1);
    });

    // --- RIGHT PANEL: 3. DEBUG VISUALIZERS ---
    this.bindCheckbox('chk-dbg-colliders', (v) => (this.debugVis.showCollisionShapes = v));
    this.bindCheckbox('chk-dbg-joints', (v) => (this.debugVis.showJoints = v));
    this.bindCheckbox('chk-dbg-com', (v) => (this.debugVis.showCenterOfMass = v));
    this.bindCheckbox('chk-dbg-vel', (v) => (this.debugVis.showVelocityVectors = v));
    this.bindCheckbox('chk-dbg-force', (v) => (this.debugVis.showForceVectors = v));
  }

  private bindKeyboardAndResize(container: HTMLElement) {
    window.addEventListener('resize', () => {
      this.camera.aspect = container.clientWidth / container.clientHeight;
      this.camera.updateProjectionMatrix();
      this.renderer.setSize(container.clientWidth, container.clientHeight);
    });

    window.addEventListener('keydown', (e) => {
      this.keys[e.code] = true;
      if (e.code === 'KeyF') {
        this.focusCameraOnCharacter();
      } else if (e.code === 'KeyR') {
        this.ragdoll.resetToOrigin();
        this.arena.resetProps();
      }
    });

    window.addEventListener('keyup', (e) => {
      this.keys[e.code] = false;
    });
  }

  private loop(now: number) {
    const rawDelta = (now - this.lastTime) / 1000;
    this.lastTime = now;
    const delta = Math.min(rawDelta, 0.05);

    // FPS Counter
    this.fpsFrameCount++;
    this.fpsAccumTime += rawDelta;
    if (this.fpsAccumTime >= 0.35) {
      this.currentFps = Math.round(this.fpsFrameCount / this.fpsAccumTime);
      this.fpsFrameCount = 0;
      this.fpsAccumTime = 0;
    }

    if (!this.isSimPaused) {
      // Ввод WASD для проверки ходьбы Active Ragdoll
      let mx = 0;
      let mz = 0;
      if (this.keys['KeyW'] || this.keys['ArrowUp']) mz -= 1;
      if (this.keys['KeyS'] || this.keys['ArrowDown']) mz += 1;
      if (this.keys['KeyA'] || this.keys['ArrowLeft']) mx -= 1;
      if (this.keys['KeyD'] || this.keys['ArrowRight']) mx += 1;

      const camForward = new THREE.Vector3();
      this.camera.getWorldDirection(camForward);
      const camYaw = Math.atan2(camForward.x, camForward.z) + Math.PI;

      // Шаг физического мира Rapier
      this.world.timestep = delta;
      this.world.step();

      // Интерактивное перетаскивание частей тела мышью
      this.manipulator.update(delta);

      // Обновление Active Ragdoll
      this.ragdoll.update(
        delta,
        {
          x: mx,
          z: mz,
          sprint: !!(this.keys['ShiftLeft'] || this.keys['ShiftRight']),
          jump: !!this.keys['Space']
        },
        camYaw,
        this.inspector.isPlayingAnim
      );

      // Обновление физических пропсов арены
      this.arena.update();
    }

    // Обновление инспектора модели и анимаций
    this.inspector.update(delta);

    // Обновление 3D дебаг-визуализаторов
    this.debugVis.update();

    // Обновление живой телеметрии в UI
    this.updateLiveTelemetryUI(delta);

    this.controls.update();
    this.renderer.render(this.scene, this.camera);

    requestAnimationFrame((t) => this.loop(t));
  }

  private updateLiveTelemetryUI(delta: number) {
    // Timeline прогресс анимации
    if (this.inspector.isPlayingAnim) {
      const prog = this.inspector.getAnimationProgress();
      this.setText('val-anim-time', `${prog.current.toFixed(2)}s / ${prog.duration.toFixed(2)}s`);
      const scrub = document.getElementById('rng-anim-scrub') as HTMLInputElement | null;
      if (scrub && !this.inspector.isPausedAnim) {
        scrub.value = prog.percent.toFixed(1);
      }
    }

    // Живая физическая телеметрия (Раздел 6)
    this.setText('dbg-fps', this.currentFps.toString());
    this.setText('dbg-phys-rate', this.isSimPaused ? 'PAUSED' : `${Math.round(1 / Math.max(0.008, delta))} Hz`);
    this.setText('dbg-bodies', this.world.bodies.len().toString());
    this.setText('dbg-joints', this.world.impulseJoints.len().toString());

    const speed = this.ragdoll.mainVelocity.length();
    this.setText('dbg-vel', `${speed.toFixed(2)} m/s`);

    const com = this.ragdoll.centerOfMass;
    this.setText('dbg-pos', `${com.x.toFixed(2)}, ${com.y.toFixed(2)}, ${com.z.toFixed(2)}`);

    const moveState = this.manipulator.isGrabbing ? `GRABBED (${this.manipulator.grabbedPartName})` : this.ragdoll.movementState;
    this.setText('dbg-move-state', moveState);

    const balEl = document.getElementById('dbg-bal-state');
    if (balEl) {
      balEl.innerText = `${this.ragdoll.balanceState} (${this.ragdoll.balanceQuality}%)`;
      balEl.style.color =
        this.ragdoll.balanceState === 'BALANCED'
          ? '#34d399'
          : this.ragdoll.balanceState === 'UNSTABLE'
          ? '#fbbf24'
          : '#f43f5e';
    }

    this.setText('dbg-cols', this.debugVis.countActiveCollisions().toString());
  }

  private setText(id: string, text: string) {
    const el = document.getElementById(id);
    if (el) el.innerText = text;
  }

  private bindCheckbox(id: string, onChange: (checked: boolean) => void) {
    const el = document.getElementById(id) as HTMLInputElement | null;
    el?.addEventListener('change', () => onChange(el.checked));
  }

  private bindSlider(sliderId: string, labelId: string, onInput: (val: number) => string) {
    const slider = document.getElementById(sliderId) as HTMLInputElement | null;
    const label = document.getElementById(labelId);
    slider?.addEventListener('input', () => {
      const num = parseFloat(slider.value);
      const formatted = onInput(num);
      if (label) label.innerText = formatted;
    });
  }
}

window.addEventListener('DOMContentLoaded', () => {
  new CharacterPlaygroundApp();
});
