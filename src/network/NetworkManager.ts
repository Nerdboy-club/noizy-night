import Peer, { DataConnection } from 'peerjs';

export interface PlayerPacket {
  type: 'PLAYER_MOVE';
  playerIndex: number;
  pos: { x: number; y: number; z: number };
  rotY: number;
  isCrouching: boolean;
  isHoldingGift: boolean;
  carriedGiftId: string | null;
  noise: number;
}

export interface GiftInteractPacket {
  type: 'GIFT_INTERACT';
  giftId: string;
  playerIndex: number;
  action: 'GRAB' | 'RELEASE';
}

export interface GameSyncPacket {
  type: 'GAME_SYNC';
  state: 'ROOFTOP' | 'APARTMENT' | 'WIN' | 'GAMEOVER';
  gifts: Array<{
    id: string;
    pos: { x: number; y: number; z: number };
    isDelivered: boolean;
  }>;
  human: {
    pos: { x: number; y: number; z: number };
    state: string;
  };
  cat: {
    pos: { x: number; y: number; z: number };
    state: string;
  };
  globalNoise: number;
}

export type NetworkPacket =
  | PlayerPacket
  | GiftInteractPacket
  | GameSyncPacket
  | { type: 'WELCOME'; playerIndex: number }
  | { type: 'PLAYER_JOINED'; playerIndex: number };

export class NetworkManager {
  public isHost: boolean = false;
  public localPlayerIndex: number = 0;
  public roomCode: string = '';
  public isConnected: boolean = false;

  private peer: Peer | null = null;
  private connections: Map<string, DataConnection> = new Map();
  private hostConnection: DataConnection | null = null;

  private onPacketCallback: ((packet: NetworkPacket) => void) | null = null;
  private onPlayerJoinedCallback: ((index: number) => void) | null = null;

  constructor(
    onPacket: (packet: NetworkPacket) => void,
    onPlayerJoined: (index: number) => void
  ) {
    this.onPacketCallback = onPacket;
    this.onPlayerJoinedCallback = onPlayerJoined;
  }

  // Создать комнату в качестве Хоста
  public createRoom(onRoomReady: (code: string) => void) {
    this.isHost = true;
    this.localPlayerIndex = 0;

    // Генерируем 4-значный код комнаты
    const code = Math.floor(1000 + Math.random() * 9000).toString();
    this.roomCode = code;

    const peerId = `elf-stealth-room-${code}`;
    this.peer = new Peer(peerId);

    this.peer.on('open', () => {
      this.isConnected = true;
      onRoomReady(code);
    });

    this.peer.on('connection', (conn) => {
      const nextIndex = this.connections.size + 1;
      if (nextIndex >= 4) {
        conn.close();
        return;
      }

      this.connections.set(conn.peer, conn);

      conn.on('open', () => {
        // Отправляем новому игроку его порядковый номер
        conn.send({ type: 'WELCOME', playerIndex: nextIndex });

        // Оповещаем остальных игроков о подключении
        this.broadcast({ type: 'PLAYER_JOINED', playerIndex: nextIndex });
        if (this.onPlayerJoinedCallback) {
          this.onPlayerJoinedCallback(nextIndex);
        }
      });

      conn.on('data', (data) => {
        const packet = data as NetworkPacket;
        // Хост ретранслирует движение игрока другим клиентам
        if (packet.type === 'PLAYER_MOVE') {
          this.broadcast(packet, conn.peer);
        }
        if (this.onPacketCallback) {
          this.onPacketCallback(packet);
        }
      });

      conn.on('close', () => {
        this.connections.delete(conn.peer);
      });
    });

    this.peer.on('error', (err) => {
      console.warn('PeerJS Host Error:', err);
    });
  }

  // Подключиться к существующей комнате в качестве Клиента
  public joinRoom(code: string, onConnected: (index: number) => void, onError: (msg: string) => void) {
    this.isHost = false;
    this.roomCode = code;

    this.peer = new Peer();

    this.peer.on('open', () => {
      const targetPeerId = `elf-stealth-room-${code}`;
      const conn = this.peer!.connect(targetPeerId);
      this.hostConnection = conn;

      conn.on('open', () => {
        this.isConnected = true;
      });

      conn.on('data', (data) => {
        const packet = data as NetworkPacket;
        if (packet.type === 'WELCOME') {
          this.localPlayerIndex = packet.playerIndex;
          onConnected(this.localPlayerIndex);
        }
        if (this.onPacketCallback) {
          this.onPacketCallback(packet);
        }
      });

      conn.on('error', (err) => {
        onError('Не удалось подключиться к комнате: ' + err);
      });
    });

    this.peer.on('error', (err) => {
      onError('Ошибка PeerJS: ' + err.message);
    });
  }

  // Отправка пакета
  public send(packet: NetworkPacket) {
    if (this.isHost) {
      this.broadcast(packet);
    } else if (this.hostConnection && this.hostConnection.open) {
      this.hostConnection.send(packet);
    }
  }

  // Ретрансляция всем подключенным (для хоста)
  private broadcast(packet: NetworkPacket, excludePeerId?: string) {
    this.connections.forEach((conn, peerId) => {
      if (peerId !== excludePeerId && conn.open) {
        conn.send(packet);
      }
    });
  }
}
