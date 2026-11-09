import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { PhysicsWorld } from './physics/PhysicsWorld';
import { SoundEngine } from './audio/SoundEngine';
import { ElfCharacter, ELF_THEMES } from './entities/ElfCharacter';
import { RooftopLevel } from './world/RooftopLevel';
import { ApartmentLevel } from './world/ApartmentLevel';
import { SantaWorkshopHub } from './world/SantaWorkshopHub';
import { SleepyHuman } from './entities/SleepyHuman';
import { PetCat } from './entities/PetCat';
import { SpookyEvents } from './systems/SpookyEvents';
import { HUD } from './ui/HUD';
import { NetworkManager, NetworkPacket } from './network/NetworkManager';
import { GiftItem } from './entities/GiftItem';

type GameState = 'START' | 'WORKSHOP_HUB' | 'ROOFTOP' | 'TRANSITION' | 'APARTMENT' | 'GAMEOVER' | 'WIN';

class Game {
  private renderer!: THREE.WebGLRenderer;
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;

  private physics!: PhysicsWorld;
  private soundEngine!: SoundEngine;
  private hud!: HUD;
  private network!: NetworkManager;

  // Отряд эльфов (до 4 игроков)
  private elves: ElfCharacter[] = [];
  private localPlayerIndex: number = 0;
  private testBotElf: ElfCharacter | null = null;

  private workshopHub!: SantaWorkshopHub;
  private rooftopLevel!: RooftopLevel;
  private apartmentLevel!: ApartmentLevel;
  private human!: SleepyHuman;
  private cat!: PetCat;
  private spookyEvents!: SpookyEvents;

  private gameState: GameState = 'START';
  private currentNoise: number = 10;
  private cameraAngleY: number = 0;
  private cameraPitch: number = 0.34;
  private cameraDistance: number = 4.2;
  private isOverShoulder: boolean = true;
  private camCollisionDist: number = 4.2;

  private lastTime: number = 0;
  private netSyncTimer: number = 0;

  private ambientLight!: THREE.HemisphereLight;
  private dirLight!: THREE.DirectionalLight;
  private composer!: EffectComposer;
  private bloomPass!: UnrealBloomPass;

  constructor() {
    this.init();
  }

  private async init() {
    const canvas = document.getElementById('game-canvas') as HTMLCanvasElement;

    this.renderer = new THREE.WebGLRenderer({
      canvas,
      antialias: true,
      powerPreference: 'high-performance'
    });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5));
    this.renderer.setSize(this.viewW(), this.viewH());
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.15;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0f172a);
    this.scene.fog = new THREE.FogExp2(0x0f172a, 0.016);

    // ПБР-окружение (IBL) для реалистичных бликов на лакированном паркете, кафеле, латунных бубенчиках и войлоке
    const pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmremGenerator.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environmentIntensity = 0.55;

    this.camera = new THREE.PerspectiveCamera(
      58,
      this.viewW() / this.viewH(),
      0.1,
      100
    );

    // Мягкий заполняющий свет (теплый верх от гирлянд + глубокий зимний полутон снизу)
    this.ambientLight = new THREE.HemisphereLight(0xffe8cc, 0x1e293b, 0.85);
    this.scene.add(this.ambientLight);

    // Направленный свет с четкими мягкими тенями 4K
    this.dirLight = new THREE.DirectionalLight(0xffedd5, 1.45);
    this.dirLight.position.set(7, 16, 8);
    this.dirLight.castShadow = true;
    this.dirLight.shadow.mapSize.width = 2048;
    this.dirLight.shadow.mapSize.height = 2048;
    this.dirLight.shadow.camera.near = 0.5;
    this.dirLight.shadow.camera.far = 42;
    const d = 15;
    this.dirLight.shadow.camera.left = -d;
    this.dirLight.shadow.camera.right = d;
    this.dirLight.shadow.camera.top = d;
    this.dirLight.shadow.camera.bottom = -d;
    this.dirLight.shadow.bias = -0.0004;
    this.dirLight.shadow.normalBias = 0.03;
    this.scene.add(this.dirLight);

    // Кинематографичный Пост-Процессинг (Мягкий Bloom-свет для гирлянд ёлки, камина и светящихся глаз эльфа)
    this.composer = new EffectComposer(this.renderer);
    this.composer.setPixelRatio(this.renderer.getPixelRatio());
    const renderPass = new RenderPass(this.scene, this.camera);
    this.composer.addPass(renderPass);

    this.bloomPass = new UnrealBloomPass(
      new THREE.Vector2(this.viewW(), this.viewH()),
      0.28, // сила мягкого свечения
      0.5,  // радиус
      1.0   // порог (светятся только гирлянды, огонь камина, глаза и звезда!)
    );
    this.composer.addPass(this.bloomPass);

    const outputPass = new OutputPass();
    this.composer.addPass(outputPass);
    // Размер выставляем ПОСЛЕ добавления всех проходов — иначе у части render-target может остаться нулевой размер
    this.composer.setPixelRatio(this.renderer.getPixelRatio());
    this.composer.setSize(this.viewW(), this.viewH());

    this.physics = new PhysicsWorld();
    await this.physics.init();

    this.soundEngine = new SoundEngine();
    this.hud = new HUD();

    // 1. Центральный Хаб: Мастерская Санты (активна на старте!)
    this.workshopHub = new SantaWorkshopHub(this.scene, this.physics, this.soundEngine);
    this.workshopHub.setVisible(true);

    // 2. Крыша и 5-комнатный Дом (скрыты, пока эльфы на Базе)
    this.rooftopLevel = new RooftopLevel(this.scene, this.physics);
    this.rooftopLevel.setVisible(false);

    this.apartmentLevel = new ApartmentLevel(this.scene, this.physics, this.soundEngine);
    this.apartmentLevel.setVisible(false);

    // 3. Создаем локального игрока (Эльф №1) в центре Мастерской Санты
    const localElf = new ElfCharacter(
      this.scene,
      this.physics,
      this.soundEngine,
      this.workshopHub.spawnPoint.clone(),
      0,
      true
    );
    this.elves.push(localElf);

    // 4. Сетевой менеджер
    this.network = new NetworkManager(
      (packet) => this.handleNetworkPacket(packet),
      (joinedIndex) => this.handlePlayerJoined(joinedIndex)
    );

    // 5. NPC в доме (Ворчливый Дед и Толстый Кот на кухне)
    this.human = new SleepyHuman(
      this.scene,
      this.soundEngine,
      this.apartmentLevel.bedroomPosition,
      this.apartmentLevel.bathroomPosition
    );
    this.human.setVisible(false);

    this.cat = new PetCat(
      this.scene,
      this.soundEngine,
      new THREE.Vector3(5.2, 0.05, 4.2) // Кот спит у кухни
    );
    this.cat.setVisible(false);

    this.spookyEvents = new SpookyEvents(this.scene, this.soundEngine);

    this.setupListeners();

    const urlParams = new URLSearchParams(window.location.search);
    const roomParam = urlParams.get('room');

    this.hud.showStartModal(
      () => {
        this.soundEngine.init();
        this.enterWorkshopHub();
      },
      () => {
        this.network.createRoom((code) => {
          this.hud.displayRoom(code);
        });
      },
      (code) => {
        this.soundEngine.init();
        this.network.joinRoom(
          code,
          (playerIndex) => {
            this.localPlayerIndex = playerIndex;
            this.hud.addPlayerBadge(playerIndex, ELF_THEMES[playerIndex % ELF_THEMES.length].name);
            this.enterWorkshopHub();
          },
          (err) => alert(err)
        );
      },
      () => {
        this.addTestBotElf();
      }
    );

    if (roomParam) {
      const input = document.getElementById('input-room-code') as HTMLInputElement | null;
      if (input) input.value = roomParam;
    }

    this.lastTime = performance.now();
    requestAnimationFrame((t) => this.loop(t));
  }

  private viewW() {
    return Math.max(1, window.innerWidth);
  }

  private viewH() {
    return Math.max(1, window.innerHeight);
  }

  private handleResize() {
    if (window.innerWidth < 2 || window.innerHeight < 2) return;
    const w = this.viewW();
    const h = this.viewH();
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
    this.composer.setPixelRatio(this.renderer.getPixelRatio());
    this.composer.setSize(w, h);
  }

  private enterWorkshopHub() {
    this.gameState = 'WORKSHOP_HUB';
    this.currentNoise = 10;

    this.rooftopLevel.setVisible(false);
    this.apartmentLevel.setVisible(false);
    this.human.setVisible(false);
    this.cat.setVisible(false);
    this.workshopHub.setVisible(true);

    // Теплое уютное освещение Фабрики Санты
    this.scene.background = new THREE.Color(0x1e1b4b);
    this.scene.fog = new THREE.FogExp2(0x1e1b4b, 0.018);
    this.ambientLight.color.setHex(0xffedd5);
    this.ambientLight.intensity = 1.15;
    this.dirLight.intensity = 1.4;
    this.dirLight.position.set(6, 16, 8);

    const localElf = this.getLocalElf();
    localElf.carriedGift = null;
    localElf.sackGifts = [];
    localElf.setPosition(this.workshopHub.spawnPoint.clone());
    this.cameraAngleY = 0;

    if (this.testBotElf) {
      this.testBotElf.carriedGift = null;
      this.testBotElf.setPosition(this.workshopHub.spawnPoint.clone().add(new THREE.Vector3(1.2, 0, 0)));
    }

    this.workshopHub.spawnOrderGiftsOnConveyor();

    const order = this.workshopHub.activeOrder;
    this.hud.setPhaseTitle('🏭 База: Мастерская Санты');
    this.hud.setObjective(
      `Заказ: «${order.address}». Погрузите подарки с конвейера в Сани (или в Мешок [R]) или откройте меню [Tab]!`
    );
  }

  private launchDeliveryRun() {
    const localElf = this.getLocalElf();
    const order = this.workshopHub.activeOrder;
    const prepackedCount = localElf.sackGifts.length;

    this.gameState = 'TRANSITION';
    this.soundEngine.playChimneyWhoosh();

    const overlay = document.getElementById('danger-overlay');
    if (overlay) {
      overlay.style.backgroundColor = '#020617';
      overlay.style.transition = 'background-color 0.35s ease';
    }

    setTimeout(() => {
      this.workshopHub.setVisible(false);
      this.rooftopLevel.setVisible(false);
      this.apartmentLevel.setVisible(true);
      this.human.setVisible(true);
      this.cat.setVisible(true);

      // Настраиваем дом под выбранный адрес (Уютный праздничный дом ИЛИ Редкий Жуткий Дом с x2 чаевыми!)
      this.apartmentLevel.configureForOrder(order.requiredGifts, order.isSpookyHouse, prepackedCount);

      // Синхронизируем ссылки на подарки в мешке игрока с подарками уровня
      localElf.sackGifts = this.apartmentLevel.gifts.filter((g) => g.isInSack);
      localElf.carriedGift = null;

      if (order.isSpookyHouse) {
        // Редкий темный/жуткий дом (как на референсе коридора с часами)
        this.scene.background = new THREE.Color(0x090a14);
        this.scene.fog = new THREE.FogExp2(0x090a14, 0.045);
        this.ambientLight.color.setHex(0x475569);
        this.ambientLight.intensity = 0.45;
        this.dirLight.intensity = 0.3;
      } else {
        // Основной режим: Светлая праздничная рождественская ночь с гирляндами без страха!
        this.scene.background = new THREE.Color(0x172554);
        this.scene.fog = new THREE.FogExp2(0x172554, 0.015);
        this.ambientLight.color.setHex(0xfffbeb);
        this.ambientLight.intensity = 1.25;
        this.dirLight.intensity = 0.95;
      }

      const entryPos = new THREE.Vector3(
        this.apartmentLevel.fireplacePosition.x + 1.6,
        0.65,
        this.apartmentLevel.fireplacePosition.z
      );
      localElf.setPosition(entryPos);

      if (this.testBotElf) {
        this.testBotElf.carriedGift = null;
        this.testBotElf.setPosition(entryPos.clone().add(new THREE.Vector3(1.1, 0, -0.8)));
      }

      this.cameraAngleY = Math.PI * 0.8;

      if (overlay) {
        overlay.style.backgroundColor = 'transparent';
      }

      this.soundEngine.playJingleBell();
      this.hud.setPhaseTitle(order.isSpookyHouse ? `👻 Жуткий Рейс (x2 Чаевые): ${order.address}` : `🎄 Рейс: ${order.address}`);
      this.hud.setObjective(
        `Дотащите все подарки (${order.requiredGifts.length} шт., включая Велосипед) под Ёлку! [R] — достать из Мешка.`
      );
      this.gameState = 'APARTMENT';
    }, 550);
  }

  private addTestBotElf() {
    if (this.testBotElf) return;
    const pos = this.getLocalElf().mesh.position.clone().add(new THREE.Vector3(1.2, 0, 0));
    this.testBotElf = new ElfCharacter(
      this.scene,
      this.physics,
      this.soundEngine,
      pos,
      1,
      false
    );
    this.elves.push(this.testBotElf);
    this.hud.addPlayerBadge(1, 'Эльф-Бот 🤖 (Напарник)');
    alert('Бот-напарник добавлен! Он автоматически поможет нести Огромный Велосипед в обёртке!');
  }

  private handlePlayerJoined(index: number) {
    if (!this.elves[index]) {
      const pos = this.workshopHub.spawnPoint.clone().add(new THREE.Vector3(index * 1.0, 0, 0));
      const remoteElf = new ElfCharacter(
        this.scene,
        this.physics,
        this.soundEngine,
        pos,
        index,
        false
      );
      this.elves[index] = remoteElf;
      this.hud.addPlayerBadge(index, ELF_THEMES[index % ELF_THEMES.length].name);
    }
  }

  private handleNetworkPacket(packet: NetworkPacket) {
    if (packet.type === 'PLAYER_MOVE') {
      const remoteElf = this.elves[packet.playerIndex];
      if (remoteElf && !remoteElf.isLocalPlayer) {
        remoteElf.syncNetworkState({
          pos: packet.pos,
          rotY: packet.rotY,
          isCrouching: packet.isCrouching,
          isHoldingGift: packet.isHoldingGift
        });
        if (packet.noise > 0) {
          this.currentNoise = Math.max(this.currentNoise, packet.noise);
        }
      }
    } else if (packet.type === 'GIFT_INTERACT') {
      const activeGifts = this.gameState === 'WORKSHOP_HUB' ? this.workshopHub.gifts : this.apartmentLevel.gifts;
      const gift = activeGifts.find((g) => g.id === packet.giftId);
      const elf = this.elves[packet.playerIndex];
      if (gift && elf) {
        if (packet.action === 'GRAB') {
          gift.carriers.add(packet.playerIndex);
          elf.carriedGift = gift;
        } else {
          gift.carriers.delete(packet.playerIndex);
          elf.carriedGift = null;
        }
      }
    }
  }

  private getLocalElf(): ElfCharacter {
    return this.elves[this.localPlayerIndex] || this.elves[0];
  }

  private openOrToggleHubModal(forceState?: boolean) {
    const localElf = this.getLocalElf();
    this.hud.toggleHubMenu(
      localElf,
      this.workshopHub.availableOrders,
      this.workshopHub.activeOrder,
      {
        onSelectOrder: (orderId) => {
          localElf.carriedGift = null;
          localElf.sackGifts = [];
          this.workshopHub.selectOrder(orderId);
          const order = this.workshopHub.activeOrder;
          this.hud.setObjective(
            `Заказ: «${order.address}». Погрузите подарки с конвейера в Сани или нажмите Отправиться в рейс!`
          );
          this.openOrToggleHubModal(true);
        },
        onRerollOrders: () => {
          localElf.carriedGift = null;
          localElf.sackGifts = [];
          this.workshopHub.generateNewOrders();
          this.workshopHub.selectOrder(this.workshopHub.availableOrders[0].id);
          this.openOrToggleHubModal(true);
        },
        onUpgradeChanged: () => {
          this.soundEngine.playJingleBell();
          localElf.updateVisualGadgets();
        },
        onLaunchNow: () => {
          this.launchDeliveryRun();
        }
      },
      forceState
    );
  }

  private setupListeners() {
    window.addEventListener('resize', () => this.handleResize());

    // Зум колесом мыши
    window.addEventListener('wheel', (e) => {
      if ((e.target as HTMLElement)?.closest('.modal-box')) return;
      this.cameraDistance = Math.max(2.0, Math.min(10, this.cameraDistance + e.deltaY * 0.004));
    }, { passive: true });

    let isDragging = false;
    let prevMouseX = 0;
    let prevMouseY = 0;

    window.addEventListener('mousedown', (e) => {
      if ((e.target as HTMLElement)?.closest('.modal-box, button, input')) return;
      isDragging = true;
      prevMouseX = e.clientX;
      prevMouseY = e.clientY;
    });

    window.addEventListener('mouseup', () => {
      isDragging = false;
    });

    window.addEventListener('mousemove', (e) => {
      if (isDragging) {
        const dx = e.clientX - prevMouseX;
        const dy = e.clientY - prevMouseY;
        this.cameraAngleY -= dx * 0.005;
        this.cameraPitch = Math.max(0.1, Math.min(1.2, this.cameraPitch + dy * 0.005));
        prevMouseX = e.clientX;
        prevMouseY = e.clientY;
      }
    });

    const btnOpenHub = document.getElementById('btn-open-hub-menu');
    btnOpenHub?.addEventListener('click', () => {
      this.openOrToggleHubModal();
    });

    window.addEventListener('keydown', (e) => {
      if (e.code === 'Tab' || e.code === 'KeyB') {
        e.preventDefault();
        this.openOrToggleHubModal();
      } else if (e.code === 'KeyE') {
        this.handleInteractKeyE();
      } else if (e.code === 'KeyR') {
        this.handleSackKeyR();
      } else if (e.code === 'KeyV') {
        this.isOverShoulder = !this.isOverShoulder;
        this.cameraDistance = this.isOverShoulder ? 4.2 : 6.5;
      }
    });
  }

  // Клавиша [R]: Сложить мелкий/шумный подарок в Мешок за спиной или достать из Мешка!
  private handleSackKeyR() {
    if (this.gameState !== 'WORKSHOP_HUB' && this.gameState !== 'APARTMENT') return;
    const localElf = this.getLocalElf();

    // 1. Если в руках уже есть подарок, который влезает в мешок — кладем его в Мешок
    if (localElf.carriedGift) {
      const gift = localElf.carriedGift;
      if (!gift.canFitInSack()) {
        this.hud.setObjective('🚲 Огромный Велосипед не влезет в мешок! Несите его в руках (вдвоём или с прокачкой хвата)!');
        return;
      }
      if (localElf.sackGifts.length >= localElf.sackCapacity) {
        this.hud.setObjective(`🎒 Мешок полон (${localElf.sackCapacity}/${localElf.sackCapacity})! Улучшите вместимость Мешка на Верстаке Базы [Tab]!`);
        return;
      }

      gift.carriers.delete(this.localPlayerIndex);
      localElf.carriedGift = null;
      gift.isInSack = true;
      gift.setVisible(false);
      localElf.sackGifts.push(gift);
      this.soundEngine.playFootstep('carpet');
      return;
    }

    // 2. Иначе проверяем, есть ли рядом на полу мелкий/шумный подарок, который еще не под ёлкой
    const levelGifts = this.gameState === 'WORKSHOP_HUB' ? this.workshopHub : this.apartmentLevel;
    const nearGift = levelGifts.getNearestGift(localElf.mesh.position, 2.2);
    if (nearGift && nearGift.canFitInSack() && !nearGift.isDelivered && localElf.sackGifts.length < localElf.sackCapacity) {
      nearGift.isInSack = true;
      nearGift.setVisible(false);
      localElf.sackGifts.push(nearGift);
      this.soundEngine.playFootstep('carpet');
      return;
    }

    // 3. Если рядом нет подарка для упаковки, а в Мешке есть подарки — достаем подарок из Мешка на пол перед собой!
    if (localElf.sackGifts.length > 0) {
      const unpacked = localElf.sackGifts.pop()!;
      unpacked.isInSack = false;
      const forward = new THREE.Vector3(0, 0, 1).applyQuaternion(localElf.mesh.quaternion);
      const dropPos = localElf.mesh.position.clone().add(forward.multiplyScalar(0.8)).setY(localElf.mesh.position.y + 0.45);
      unpacked.setPosition(dropPos);
      unpacked.setVisible(true);
      this.soundEngine.playThud(0.35);
    }
  }

  // Клавиша [E]: Взять/Положить подарок в руки, открыть Доску/Верстак или улететь в Рейс / на Базу
  private handleInteractKeyE() {
    const localElf = this.getLocalElf();

    if (this.gameState === 'WORKSHOP_HUB') {
      // Если стоим у Саней и не держим подарок — вылетаем в рейс!
      const station = this.workshopHub.getNearbyStation(localElf.mesh.position);
      if (!localElf.carriedGift && (station === 'ORDER_BOARD' || station === 'WORKBENCH')) {
        this.openOrToggleHubModal(true);
        return;
      }
      if (!localElf.carriedGift && station === 'SLEIGH') {
        this.launchDeliveryRun();
        return;
      }
    }

    if (this.gameState === 'APARTMENT') {
      // Если все подарки доставлены под ёлку и мы у камина — улетаем обратно на Базу за чаевыми!
      const allDone = this.apartmentLevel.gifts.every((g) => g.isDelivered);
      if (allDone && this.apartmentLevel.isNearFireplaceExit(localElf.mesh.position)) {
        this.completeRunAndReturnToHub();
        return;
      }
    }

    // Взять / отпустить подарок в руках (работает и на Базе Санты, и в Доме!)
    if (this.gameState === 'WORKSHOP_HUB' || this.gameState === 'APARTMENT') {
      if (localElf.carriedGift) {
        const gift = localElf.carriedGift;
        gift.carriers.delete(this.localPlayerIndex);
        localElf.carriedGift = null;

        if (this.testBotElf && this.testBotElf.carriedGift === gift) {
          gift.carriers.delete(1);
          this.testBotElf.carriedGift = null;
        }

        this.soundEngine.playThud(0.4);
        this.network.send({
          type: 'GIFT_INTERACT',
          giftId: gift.id,
          playerIndex: this.localPlayerIndex,
          action: 'RELEASE'
        });
      } else {
        const nearest =
          this.gameState === 'WORKSHOP_HUB'
            ? this.workshopHub.getNearestGift(localElf.mesh.position)
            : this.apartmentLevel.getNearestGift(localElf.mesh.position);

        if (nearest) {
          nearest.carriers.add(this.localPlayerIndex);
          localElf.carriedGift = nearest;

          if (nearest.type === 'HUGE' && this.testBotElf) {
            nearest.carriers.add(1);
            this.testBotElf.carriedGift = nearest;
          }

          this.soundEngine.playFootstep('carpet');
          this.network.send({
            type: 'GIFT_INTERACT',
            giftId: nearest.id,
            playerIndex: this.localPlayerIndex,
            action: 'GRAB'
          });
        }
      }
    }
  }

  private loop(currentTime: number) {
    const delta = Math.min((currentTime - this.lastTime) / 1000, 0.1);
    this.lastTime = currentTime;

    if (this.gameState !== 'START') {
      this.update(delta);
    }

    this.render();
    requestAnimationFrame((t) => this.loop(t));
  }

  private update(delta: number) {
    this.physics.step(delta);
    this.currentNoise = Math.max(10, this.currentNoise - delta * 18);

    const localElf = this.getLocalElf();
    const elfPos = localElf.mesh.position;

    // --- ФАЗА 1: ЦЕНТРАЛЬНЫЙ ХАБ «МАСТЕРСКАЯ САНТЫ» ---
    if (this.gameState === 'WORKSHOP_HUB') {
      localElf.update(delta, this.cameraAngleY, 'wood', false, 10);

      if (this.testBotElf && this.testBotElf.carriedGift) {
        const botOffset = new THREE.Vector3(-1.1, 0, 0).applyQuaternion(localElf.mesh.quaternion);
        this.testBotElf.setPosition(localElf.mesh.position.clone().add(botOffset));
        this.testBotElf.mesh.rotation.y = localElf.mesh.rotation.y;
      }

      const hubStatus = this.workshopHub.update(delta, localElf.gripLevel, localElf.sackGifts.length);
      const station = this.workshopHub.getNearbyStation(elfPos);
      const nearGift = this.workshopHub.getNearestGift(elfPos);

      let promptText = '';
      if (localElf.carriedGift) {
        promptText = localElf.carriedGift.canFitInSack()
          ? '[E] Опустить в Сани  |  [R] Убрать в Мешок за спину 🎒'
          : '[E] Погрузить Велосипед в зеленый круг у Саней 🛷';
      } else if (nearGift) {
        promptText = nearGift.canFitInSack()
          ? `[E] Взять ${nearGift.label}  |  [R] Сразу в Мешок 🎒`
          : `[E] Взять ${nearGift.label} (Кооп / Сила Хвата)`;
      } else if (station === 'SLEIGH') {
        promptText = hubStatus.readyToLaunch
          ? '🛷 Все подарки погружены! Нажмите [E] для вылета в Рейс!'
          : '🛷 Нажмите [E], чтобы отправиться в Рейс сейчас!';
      } else if (station === 'ORDER_BOARD') {
        promptText = '📋 Нажмите [E] или [Tab] — Доска Заказов (Выбор адреса)';
      } else if (station === 'WORKBENCH') {
        promptText = '🔧 Нажмите [E] или [Tab] — Верстак Прокачки (Джетпак, Обувь, Мешок)';
      }

      const giftBadgesHtml = this.workshopHub.gifts
        .map((g) => {
          const icon = g.type === 'SMALL' ? '📦' : g.type === 'NOISY' ? '⏰' : '🚲';
          const status = g.isInSack ? '🎒' : g.isLoadedInSleigh ? '✅' : '⚪';
          return `<span>${icon}[${status}]</span>`;
        })
        .join(' ');

      this.hud.update(
        10,
        false,
        localElf.isCrouching,
        false,
        hubStatus.loadedInSleighCount,
        hubStatus.totalOrderGifts,
        promptText,
        localElf,
        true,
        giftBadgesHtml
      );
    }

    // --- ФАЗА 2: РЕЙС ПО АДРЕСУ (ВНУТРИ 5-КОМНАТНОГО ДОМА) ---
    else if (this.gameState === 'APARTMENT') {
      const surfaceType = this.apartmentLevel.getSurfaceType(elfPos);
      const isCreaky = this.apartmentLevel.isCreaky(elfPos);
      const isHidden = this.apartmentLevel.isHidden(elfPos) && localElf.isCrouching;

      const { noiseGenerated } = localElf.update(
        delta,
        this.cameraAngleY,
        surfaceType,
        isCreaky,
        this.currentNoise
      );
      if (noiseGenerated > 0) {
        this.currentNoise = Math.max(this.currentNoise, noiseGenerated);
      }

      this.checkHelpTeammate(localElf, delta);

      if (this.testBotElf && this.testBotElf.carriedGift) {
        const botOffset = new THREE.Vector3(-1.1, 0, 0).applyQuaternion(localElf.mesh.quaternion);
        this.testBotElf.setPosition(localElf.mesh.position.clone().add(botOffset));
        this.testBotElf.mesh.rotation.y = localElf.mesh.rotation.y;
      }

      const { noiseFromClatter, deliveredCount, totalGiftsCount, allDelivered } = this.apartmentLevel.update(
        delta,
        localElf.gripLevel
      );
      if (noiseFromClatter > 0) {
        this.currentNoise = Math.max(this.currentNoise, noiseFromClatter);
      }

      // Ворчливый Дед
      const { stateName } = this.human.update(delta, this.currentNoise);
      const spotted = this.human.checkVisionCone(elfPos, isHidden);
      if (spotted) {
        this.triggerGameOver('Ворчливый Дед в тапках-оленях заметил вас фонариком! Вы сбежали обратно на Базу.');
        return;
      }

      // Толстый Кот на кухне
      const { noiseAdded } = this.cat.update(delta, this.currentNoise, elfPos);
      if (noiseAdded > 0) {
        this.currentNoise = Math.max(this.currentNoise, this.currentNoise + noiseAdded);
      }

      // Жуткие события включаются только если выбран редкий «Тёмный / Жуткий Дом»!
      if (this.apartmentLevel.isSpookyHouse) {
        this.spookyEvents.update(delta);
      }

      if (allDelivered) {
        this.hud.setObjective(
          '✅ Все подарки уложены под Ёлкой! Вернитесь к Камину [E], чтобы эвакуироваться на Базу Санты за чаевыми!'
        );
        if (this.apartmentLevel.isNearFireplaceExit(elfPos)) {
          this.completeRunAndReturnToHub();
          return;
        }
      }

      let promptText = '';
      if (allDelivered && this.apartmentLevel.isNearFireplaceExit(elfPos)) {
        promptText = '🔥 Нажмите [E] — Вернуться на Базу Санты с чаевыми!';
      } else if (localElf.carriedGift) {
        promptText = `[E] Положить «${localElf.carriedGift.label}» (Целостность: ${localElf.carriedGift.integrity}%)`;
      } else {
        const nearGift = this.apartmentLevel.getNearestGift(elfPos);
        if (nearGift) {
          promptText = nearGift.canFitInSack()
            ? `[E] Взять ${nearGift.label} (${nearGift.integrity}%)  |  [R] В Мешок 🎒`
            : `[E] Тащить ${nearGift.label} (Целостность бумаги: ${nearGift.integrity}%)`;
        } else if (localElf.sackGifts.length > 0) {
          promptText = `🎒 В Мешке ${localElf.sackGifts.length} подарка — нажмите [R] у Ёлки, чтобы выложить!`;
        }
      }

      const giftBadgesHtml = this.apartmentLevel.gifts
        .map((g) => {
          const icon = g.type === 'SMALL' ? '📦' : g.type === 'NOISY' ? '⏰' : '🚲';
          const status = g.isDelivered ? '✅' : g.isInSack ? '🎒' : `${g.integrity}%`;
          return `<span>${icon}[${status}]</span>`;
        })
        .join(' ');

      this.hud.update(
        this.currentNoise,
        isHidden,
        localElf.isCrouching,
        stateName === 'ALERT_SPOTTED',
        deliveredCount,
        totalGiftsCount,
        promptText,
        localElf,
        false,
        giftBadgesHtml
      );
    }

    // Сетевая синхронизация
    this.netSyncTimer += delta;
    if (this.netSyncTimer > 0.033 && this.network.isConnected) {
      this.netSyncTimer = 0;
      const bPos = localElf.body.translation();
      this.network.send({
        type: 'PLAYER_MOVE',
        playerIndex: this.localPlayerIndex,
        pos: { x: bPos.x, y: bPos.y, z: bPos.z },
        rotY: localElf.mesh.rotation.y,
        isCrouching: localElf.isCrouching,
        isHoldingGift: localElf.isHoldingGift,
        carriedGiftId: localElf.carriedGift ? localElf.carriedGift.id : null,
        noise: this.currentNoise
      });
    }

    this.updateCamera(delta);
  }

  private checkHelpTeammate(localElf: ElfCharacter, delta: number) {
    if (!localElf.isGrabbing) return;
    for (const other of this.elves) {
      if (other === localElf) continue;
      const dist = localElf.mesh.position.distanceTo(other.mesh.position);
      if (dist < 2.1) {
        const pullPos = other.mesh.position.clone().lerp(localElf.mesh.position, delta * 3.5);
        other.setPosition(pullPos);
        other.stamina = Math.min(100, other.stamina + delta * 30);
      }
    }
  }

  private updateCamera(delta: number) {
    const localElf = this.getLocalElf();
    const shoulderRight = this.isOverShoulder
      ? new THREE.Vector3(Math.cos(this.cameraAngleY) * 0.32, 0.85, -Math.sin(this.cameraAngleY) * 0.32)
      : new THREE.Vector3(0, 0.8, 0);
    const targetPos = localElf.mesh.position.clone().add(shoulderRight);
    targetPos.y -= localElf.cameraKick;

    const dir = new THREE.Vector3(
      Math.sin(this.cameraAngleY) * Math.cos(this.cameraPitch),
      Math.sin(this.cameraPitch),
      Math.cos(this.cameraAngleY) * Math.cos(this.cameraPitch)
    );

    // Коллизия камеры: луч от эльфа к желаемой позиции по статическим коллайдерам (стены, камин, ёлка)
    let allowed = this.cameraDistance;
    if (this.physics?.world) {
      const R = this.physics.getRapier();
      const ray = new R.Ray(
        { x: targetPos.x, y: targetPos.y, z: targetPos.z },
        { x: dir.x, y: dir.y, z: dir.z }
      );
      const hit = this.physics.world.castRay(
        ray,
        this.cameraDistance + 0.4,
        true,
        R.QueryFilterFlags.EXCLUDE_DYNAMIC | R.QueryFilterFlags.EXCLUDE_KINEMATIC | R.QueryFilterFlags.EXCLUDE_SENSORS,
        undefined,
        undefined,
        undefined,
        // низкую мебель и пол игнорируем, чтобы камера не прыгала
        (c) => c.translation().y >= 1.0
      );
      // Если луч стартует внутри коллайдера (toi≈0, например у камина) — это не препятствие для камеры
      if (hit && hit.timeOfImpact > 0.15) {
        allowed = Math.max(1.6, Math.min(this.cameraDistance, hit.timeOfImpact - 0.35));
      }
    }

    // Быстро приближаемся при препятствии, плавно отъезжаем обратно (независимо от FPS)
    const dt = Math.min(delta, 0.1);
    const kDist = allowed < this.camCollisionDist ? 1 - Math.exp(-dt * 30) : 1 - Math.exp(-dt * 4);
    this.camCollisionDist += (allowed - this.camCollisionDist) * kDist;
    if (this.camCollisionDist > this.cameraDistance) this.camCollisionDist = this.cameraDistance;

    const desired = targetPos.clone().addScaledVector(dir, this.camCollisionDist);
    this.camera.position.lerp(desired, 1 - Math.exp(-dt * 11));
    this.camera.lookAt(targetPos);
  }

  private triggerGameOver(reason: string) {
    this.gameState = 'GAMEOVER';
    this.soundEngine.playHorrorStinger();
    const localElf = this.getLocalElf();
    // Небольшой штраф к прочности снаряги, но возвращаемся на Базу и сохраняем все апгрейды!
    localElf.gadgetDurability = Math.max(25, localElf.gadgetDurability - 20);
    this.hud.showGameOver(reason, () => {
      this.enterWorkshopHub();
    });
  }

  private completeRunAndReturnToHub() {
    this.gameState = 'WIN';
    this.soundEngine.playJingleBell();
    this.soundEngine.playJingleBell();

    const localElf = this.getLocalElf();
    const order = this.workshopHub.activeOrder;

    // Подсчет средней целостности упаковки доставленных подарков
    const totalIntegrity = this.apartmentLevel.gifts.reduce((acc, g) => acc + g.integrity, 0);
    const avgIntegrity = Math.round(totalIntegrity / Math.max(1, this.apartmentLevel.gifts.length));

    const earnedTips = Math.round(order.baseReward * (avgIntegrity / 100) * order.tipMultiplier);
    localElf.tipsWallet += earnedTips;
    localElf.completedRuns++;

    // Генерируем свежие адреса на Доске Заказов для следующей смены!
    this.workshopHub.generateNewOrders();
    this.workshopHub.activeOrder = this.workshopHub.availableOrders[0];

    const summaryHtml = `
      Адрес <strong>«${order.address}»</strong> успешно укомплектован!<br>
      📦 Сохранность упаковки: <strong>${avgIntegrity}%</strong><br>
      💰 Заработано за рейс: <strong style="color: #34d399; font-size: 18px;">+$${earnedTips}</strong> (Всего в кошельке: <strong>$${localElf.tipsWallet}</strong>)<br><br>
      Возвращайтесь в Мастерскую Санты за новой партией подарков и прокачкой гаджетов!
    `;

    this.hud.showWinModal(summaryHtml, () => {
      this.enterWorkshopHub();
      // Сразу открываем окно Доски Заказов и Верстака, чтобы игрок мог прокачать Джетпак/Обувь/Мешок!
      setTimeout(() => this.openOrToggleHubModal(true), 250);
    });
  }

  private render() {
    if (window.innerWidth < 2 || window.innerHeight < 2) return;
    this.composer.render();
  }
}

window.addEventListener('DOMContentLoaded', () => {
  new Game();
});
