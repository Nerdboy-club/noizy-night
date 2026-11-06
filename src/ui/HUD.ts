import { ElfCharacter } from '../entities/ElfCharacter';
import { DeliveryOrder } from '../world/SantaWorkshopHub';

export class HUD {
  private phaseTitleEl: HTMLElement | null;
  private noiseFillEl: HTMLElement | null;
  private noiseDbEl: HTMLElement | null;
  private stealthBadgeEl: HTMLElement | null;
  private stealthTextEl: HTMLElement | null;
  private objectiveTextEl: HTMLElement | null;
  private dangerOverlayEl: HTMLElement | null;
  private giftStatusEl: HTMLElement | null;
  private giftBoxTitleEl: HTMLElement | null;
  private giftDetailsEl: HTMLElement | null;
  private giftPromptEl: HTMLElement | null;
  private staminaFillEl: HTMLElement | null;
  private staminaValEl: HTMLElement | null;
  private walletTipsEl: HTMLElement | null;
  private sackStatusEl: HTMLElement | null;
  private lblBootsLvlEl: HTMLElement | null;

  private startModalEl: HTMLElement | null;
  private gameoverModalEl: HTMLElement | null;
  private winModalEl: HTMLElement | null;
  private hubModalEl: HTMLElement | null;

  private roomInfoEl: HTMLElement | null;
  private displayRoomCodeEl: HTMLElement | null;
  private playersListEl: HTMLElement | null;

  private displayedNoise: number = 10;

  constructor() {
    this.phaseTitleEl = document.getElementById('phase-title');
    this.noiseFillEl = document.getElementById('noise-fill');
    this.noiseDbEl = document.getElementById('noise-db');
    this.stealthBadgeEl = document.getElementById('stealth-badge');
    this.stealthTextEl = document.getElementById('stealth-text');
    this.objectiveTextEl = document.getElementById('objective-text');
    this.dangerOverlayEl = document.getElementById('danger-overlay');
    this.giftStatusEl = document.getElementById('gift-status');
    this.giftBoxTitleEl = document.getElementById('gift-box-title');
    this.giftDetailsEl = document.getElementById('gift-details');
    this.giftPromptEl = document.getElementById('gift-prompt');
    this.staminaFillEl = document.getElementById('stamina-fill');
    this.staminaValEl = document.getElementById('stamina-val');
    this.walletTipsEl = document.getElementById('wallet-tips');
    this.sackStatusEl = document.getElementById('sack-status');
    this.lblBootsLvlEl = document.getElementById('lbl-boots-lvl');

    this.startModalEl = document.getElementById('start-modal');
    this.gameoverModalEl = document.getElementById('gameover-modal');
    this.winModalEl = document.getElementById('win-modal');
    this.hubModalEl = document.getElementById('hub-modal');

    this.roomInfoEl = document.getElementById('room-info');
    this.displayRoomCodeEl = document.getElementById('display-room-code');
    this.playersListEl = document.getElementById('players-list');
  }

  public update(
    currentNoise: number,
    isHidden: boolean,
    isCrouching: boolean,
    isAlert: boolean,
    deliveredCount: number = 0,
    totalCount: number = 3,
    promptText: string = '',
    elf?: ElfCharacter,
    isHubMode: boolean = false,
    giftIconsHtml?: string
  ) {
    this.displayedNoise += (currentNoise - this.displayedNoise) * 0.15;
    const clampedNoise = Math.max(8, Math.min(100, this.displayedNoise));

    if (this.noiseFillEl) {
      this.noiseFillEl.style.width = `${clampedNoise}%`;
    }
    if (this.noiseDbEl) {
      this.noiseDbEl.innerText = `${Math.round(clampedNoise)} dB`;
    }

    if (elf) {
      if (this.walletTipsEl) this.walletTipsEl.innerText = `$${elf.tipsWallet}`;
      if (this.sackStatusEl) this.sackStatusEl.innerText = `${elf.sackGifts.length} / ${elf.sackCapacity}`;
      if (this.lblBootsLvlEl) this.lblBootsLvlEl.innerText = elf.silentBootsLevel.toString();

      const barVal = elf.jetpackLevel > 0 ? elf.jetpackFuel : elf.stamina;
      const clampedBar = Math.max(0, Math.min(100, barVal));
      if (this.staminaFillEl) {
        this.staminaFillEl.style.width = `${clampedBar}%`;
      }
      if (this.staminaValEl) {
        this.staminaValEl.innerText =
          elf.jetpackLevel > 0
            ? `🚀 ${Math.round(elf.jetpackFuel)}% | 💪 ${Math.round(elf.stamina)}%`
            : `${Math.round(elf.stamina)}%`;
      }
    }

    if (this.dangerOverlayEl) {
      const dangerOpacity = isAlert ? 0.8 : Math.max(0, (clampedNoise - 42) / 70);
      this.dangerOverlayEl.style.boxShadow = `inset 0 0 ${120 * dangerOpacity}px rgba(239, 68, 68, ${dangerOpacity})`;
    }

    if (this.stealthBadgeEl && this.stealthTextEl) {
      this.stealthBadgeEl.className = 'status-badge';

      if (isHubMode) {
        this.stealthTextEl.innerHTML = 'На Базе Санты 🏭';
      } else if (isAlert) {
        this.stealthBadgeEl.classList.add('alert');
        this.stealthTextEl.innerHTML = 'ТРЕВОГА! ДЕД ЗАМЕТИЛ! 🚨';
      } else if (isHidden) {
        this.stealthBadgeEl.classList.add('hidden');
        this.stealthTextEl.innerHTML = 'В УКРЫТИИ (Под мебелью) 📦';
      } else if (clampedNoise > 45) {
        this.stealthBadgeEl.classList.add('caution');
        this.stealthTextEl.innerHTML = 'СЛИШКОМ ШУМНО! 👂';
      } else if (isCrouching) {
        this.stealthTextEl.innerHTML = 'На цыпочках (Тихо) 🤫';
      } else {
        this.stealthTextEl.innerHTML = 'В рейсе 🧝‍♂️';
      }
    }

    if (this.giftBoxTitleEl) {
      this.giftBoxTitleEl.innerText = isHubMode ? 'Погрузка в Сани' : 'Доставка под Ёлку';
    }
    if (this.giftStatusEl) {
      this.giftStatusEl.innerText = isHubMode
        ? `${deliveredCount} / ${totalCount} погружено`
        : `${deliveredCount} / ${totalCount} под ёлкой`;
    }
    if (this.giftDetailsEl && giftIconsHtml) {
      this.giftDetailsEl.innerHTML = giftIconsHtml;
    }
    if (this.giftPromptEl) {
      this.giftPromptEl.innerText = promptText;
    }
  }

  public setPhaseTitle(title: string) {
    if (this.phaseTitleEl) {
      this.phaseTitleEl.innerText = title;
    }
  }

  public setObjective(text: string) {
    if (this.objectiveTextEl) {
      this.objectiveTextEl.innerText = text;
    }
  }

  public isHubMenuOpen(): boolean {
    return !!this.hubModalEl?.classList.contains('active');
  }

  public toggleHubMenu(
    elf: ElfCharacter,
    orders: DeliveryOrder[],
    activeOrder: DeliveryOrder,
    callbacks: {
      onSelectOrder: (orderId: string) => void;
      onRerollOrders: () => void;
      onUpgradeChanged: () => void;
      onLaunchNow: () => void;
    },
    forceState?: boolean
  ) {
    if (!this.hubModalEl) return;
    const nextOpen = forceState !== undefined ? forceState : !this.hubModalEl.classList.contains('active');
    if (!nextOpen) {
      this.hubModalEl.classList.remove('active');
      return;
    }

    this.hubModalEl.classList.add('active');
    this.renderHubModalContents(elf, orders, activeOrder, callbacks);

    const btnClose = document.getElementById('btn-close-hub');
    if (btnClose) {
      btnClose.onclick = () => this.hubModalEl?.classList.remove('active');
    }

    const btnReroll = document.getElementById('btn-reroll-orders');
    if (btnReroll) {
      btnReroll.onclick = () => {
        callbacks.onRerollOrders();
      };
    }

    const btnLaunch = document.getElementById('btn-launch-run-now');
    if (btnLaunch) {
      btnLaunch.onclick = () => {
        this.hubModalEl?.classList.remove('active');
        callbacks.onLaunchNow();
      };
    }
  }

  public renderHubModalContents(
    elf: ElfCharacter,
    orders: DeliveryOrder[],
    activeOrder: DeliveryOrder,
    callbacks: {
      onSelectOrder: (orderId: string) => void;
      onRerollOrders: () => void;
      onUpgradeChanged: () => void;
      onLaunchNow: () => void;
    }
  ) {
    const walletEl = document.getElementById('hub-wallet-val');
    if (walletEl) walletEl.innerText = `$${elf.tipsWallet}`;

    // 1. Рендерим карточки Адресов / Заказов
    const listContainer = document.getElementById('orders-list-container');
    if (listContainer) {
      listContainer.innerHTML = '';
      orders.forEach((order) => {
        const isSelected = order.id === activeOrder.id;
        const card = document.createElement('div');
        card.style.cssText = `
          padding: 10px 12px;
          border-radius: 12px;
          cursor: pointer;
          transition: all 0.15s;
          background: ${isSelected ? 'rgba(234, 179, 8, 0.2)' : 'rgba(30, 41, 59, 0.85)'};
          border: 2px solid ${
            isSelected ? '#facc15' : order.isSpookyHouse ? 'rgba(168, 85, 247, 0.5)' : 'rgba(255,255,255,0.08)'
          };
        `;
        const giftBadges = order.requiredGifts
          .map((g) => (g === 'SMALL' ? '📦 Мелкий' : g === 'NOISY' ? '⏰ Шумный' : '🚲 Велосипед'))
          .join(' + ');

        card.innerHTML = `
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
            <strong style="font-size: 12px; color: ${order.isSpookyHouse ? '#e9d5ff' : '#fef08a'};">
              ${isSelected ? '✅ ' : ''}${order.address}
            </strong>
            <span style="font-size: 11px; font-weight: 800; color: #34d399;">
              ~$${order.baseReward} (x${order.tipMultiplier})
            </span>
          </div>
          <div style="font-size: 11px; color: #cbd5e1; margin-bottom: 6px; line-height: 1.35;">
            ${order.description}
          </div>
          <div style="font-size: 10px; color: #93c5fd; font-weight: 700;">
            Груз: ${giftBadges}
          </div>
        `;
        card.onclick = () => {
          callbacks.onSelectOrder(order.id);
        };
        listContainer.appendChild(card);
      });
    }

    // 2. Обновляем кнопки Верстака Прокачки
    const setText = (id: string, val: string) => {
      const el = document.getElementById(id);
      if (el) el.innerText = val;
    };

    setText('upg-jetpack-lvl', elf.jetpackLevel.toString());
    setText('upg-boots-lvl', elf.silentBootsLevel.toString());
    setText('upg-sack-lvl', elf.sackCapacity.toString());
    setText('upg-grip-lvl', elf.gripLevel.toString());
    setText('upg-durability-val', `${Math.round(elf.gadgetDurability)}%`);

    const jetCost = 100 + elf.jetpackLevel * 60;
    const bootsCost = 75 + elf.silentBootsLevel * 50;
    const sackCost = 85 + (elf.sackCapacity - 1) * 55;
    const gripCost = 80 + elf.gripLevel * 50;

    const bindUpgradeBtn = (btnId: string, cost: number, currentLvl: number, maxLvl: number, onBuy: () => void) => {
      const btn = document.getElementById(btnId) as HTMLButtonElement | null;
      if (!btn) return;
      if (currentLvl >= maxLvl) {
        btn.innerText = 'МАКС.';
        btn.disabled = true;
        btn.style.opacity = '0.5';
      } else {
        btn.innerText = `$${cost}`;
        btn.disabled = elf.tipsWallet < cost;
        btn.style.opacity = elf.tipsWallet < cost ? '0.45' : '1';
        btn.onclick = () => {
          if (elf.tipsWallet >= cost && currentLvl < maxLvl) {
            elf.tipsWallet -= cost;
            onBuy();
            callbacks.onUpgradeChanged();
            this.renderHubModalContents(elf, orders, activeOrder, callbacks);
          }
        };
      }
    };

    bindUpgradeBtn('btn-buy-jetpack', jetCost, elf.jetpackLevel, 5, () => {
      elf.jetpackLevel++;
      elf.jetpackFuel = 100;
    });
    bindUpgradeBtn('btn-buy-boots', bootsCost, elf.silentBootsLevel, 5, () => {
      elf.silentBootsLevel++;
    });
    bindUpgradeBtn('btn-buy-sack', sackCost, elf.sackCapacity, 5, () => {
      elf.sackCapacity++;
    });
    bindUpgradeBtn('btn-buy-grip', gripCost, elf.gripLevel, 5, () => {
      elf.gripLevel++;
    });

    const btnRepair = document.getElementById('btn-buy-repair') as HTMLButtonElement | null;
    if (btnRepair) {
      const needsRepair = elf.gadgetDurability < 99 || elf.jetpackFuel < 99;
      btnRepair.innerText = needsRepair ? '$25' : 'ОК';
      btnRepair.disabled = !needsRepair || elf.tipsWallet < 25;
      btnRepair.style.opacity = !needsRepair || elf.tipsWallet < 25 ? '0.45' : '1';
      btnRepair.onclick = () => {
        if (elf.tipsWallet >= 25 && needsRepair) {
          elf.tipsWallet -= 25;
          elf.gadgetDurability = 100;
          elf.jetpackFuel = 100;
          callbacks.onUpgradeChanged();
          this.renderHubModalContents(elf, orders, activeOrder, callbacks);
        }
      };
    }
  }

  public displayRoom(code: string) {
    if (this.roomInfoEl && this.displayRoomCodeEl) {
      this.roomInfoEl.style.display = 'block';
      this.displayRoomCodeEl.innerText = code;
    }
  }

  public addPlayerBadge(index: number, name: string) {
    if (this.playersListEl) {
      const badge = document.createElement('span');
      badge.className = 'status-badge';
      badge.style.fontSize = '11px';
      badge.style.padding = '4px 10px';
      badge.innerText = `🧝‍♂️ Игрок ${index + 1}: ${name}`;
      this.playersListEl.appendChild(badge);
    }
  }

  public showStartModal(
    onStart: () => void,
    onCreateRoom: () => void,
    onJoinRoom: (code: string) => void,
    onAddTestElf: () => void
  ) {
    if (this.startModalEl) {
      this.startModalEl.classList.add('active');

      const btnStart = document.getElementById('btn-start');
      if (btnStart) {
        btnStart.onclick = () => {
          this.startModalEl?.classList.remove('active');
          onStart();
        };
      }

      const btnCreate = document.getElementById('btn-create-room');
      if (btnCreate) {
        btnCreate.onclick = () => onCreateRoom();
      }

      const btnJoin = document.getElementById('btn-join-room');
      const inputCode = document.getElementById('input-room-code') as HTMLInputElement | null;
      if (btnJoin && inputCode) {
        btnJoin.onclick = () => {
          const code = inputCode.value.trim();
          if (code) {
            onJoinRoom(code);
            this.startModalEl?.classList.remove('active');
          }
        };
      }

      const btnTestElf = document.getElementById('btn-add-test-elf');
      if (btnTestElf) {
        btnTestElf.onclick = () => onAddTestElf();
      }
    }
  }

  public showGameOver(reason: string, onReturnToHub: () => void) {
    if (this.gameoverModalEl) {
      const reasonEl = document.getElementById('gameover-reason');
      if (reasonEl) reasonEl.innerText = reason;
      this.gameoverModalEl.classList.add('active');

      const btn = document.getElementById('btn-restart');
      if (btn) {
        btn.onclick = () => {
          this.gameoverModalEl?.classList.remove('active');
          onReturnToHub();
        };
      }
    }
  }

  public showWinModal(summaryHtml: string, onReturnToHub: () => void) {
    if (this.winModalEl) {
      const summaryEl = document.getElementById('win-summary-text');
      if (summaryEl) summaryEl.innerHTML = summaryHtml;
      this.winModalEl.classList.add('active');
      const btn = document.getElementById('btn-win-restart');
      if (btn) {
        btn.onclick = () => {
          this.winModalEl?.classList.remove('active');
          onReturnToHub();
        };
      }
    }
  }
}
