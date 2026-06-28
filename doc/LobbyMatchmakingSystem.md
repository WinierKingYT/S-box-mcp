# Lobi, Eşleştirme ve Ağ İletişimi Tasarım Belgesi (Ultimate LobbyMatchmakingSystem)

Bu belge; s&box Steam lobi entegrasyonunu, oda kodu el sıkışmalarını, server browser veri paket yapılarını ve ağ senkronizasyon tick-rate frekanslarını tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin ağ bağlantılarını ve lobi sunucu yönetimlerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    InLobby,
    LoadingMap,
    InGame
}

/// <summary>
/// Ağ bağlantısını ve lobi yönetimini sağlayan ana arayüz.
/// </summary>
public interface INetworkManager
{
    /// <summary>
    /// Mevcut bağlantı durumunu döner.
    /// </summary>
    ConnectionStatus Status { get; }

    /// <summary>
    /// Yeni bir Steam lobisi oluşturur (Host).
    /// </summary>
    void CreateLobby(int maxPlayers, bool isPrivate);

    /// <summary>
    /// 4 haneli oda kodu veya Steam Lobby ID kullanarak lobiye katılır.
    /// </summary>
    void JoinLobby(string lobbyCode);

    /// <summary>
    /// Mevcut lobiden veya oyundan güvenli bir şekilde ayrılır.
    /// </summary>
    void LeaveLobby();

    /// <summary>
    /// Sunucunun bağlı tüm istemcilere harita yükleme emri göndermesini sağlar.
    /// </summary>
    void BroadcastMapTransition(int mapSeed);
}
```

---

## 2. 4 Haneli Oda Kodu Çözümleme ve El Sıkışma (Lobby Code Exchange)

Oyuncuların Steam ID yerine basit oda kodlarıyla birbirlerine bağlanabilmesi için sunucu tarafındaki eşleme algoritması:

1. **Oda Kodu Üretimi:** Sunucu lobi oluşturduğunda, Steam Lobby ID (64-bit) verisini 4 haneli alfanümerik (Örn: `A8B3`) benzersiz bir koda dönüştürür.
2. **Kayıt ve Saklama:** Bu kod, merkezi Black Friday lobi sunucusunda (Redis veya Postgres lobi DB) eşleştirilerek saklanır.
3. **Çözümleme ve Yönlendirme:** İstemci `A8B3` kodunu girdiğinde:
   - İstemci merkezi lobi sunucusuna `GetLobbySteamId("A8B3")` sorgusu gönderir.
   - Sunucu 64-bit `SteamLobbyId` değerini döner.
   - İstemci s&box `Networking.Connect(SteamLobbyId)` çağrısını başlatarak doğrudan P2P bağlantı kurar.

---

## 3. Ağ Senkronizasyon Frekansları Şartnamesi (Network Tick Rates)

Ağ bant genişliğini optimize etmek ve gecikmeleri (ping spikes) en aza indirmek için s&box ağ katmanında varlıkların güncelleme frekansları tablosu:

| Varlık Türü (Network Entity) | Senkronizasyon Hızı (Tick Rate) | Tahmin Tipi (Client-Side Prediction) | Replikasyon Yöntemi |
|------------------------------|--------------------------------|--------------------------------------|---------------------|
| Oyuncu Karakteri (Player)    | \(30\text{ Hz}\) (saniyede 30) | Full Prediction (Yarı-Authoritative) | `[Net]` Variables & Input States |
| Alışveriş Arabası (Trolley)   | \(30\text{ Hz}\)               | Full Physics Prediction               | Rigidbody State Interpolation |
| Güvenlik Robotları (AI)      | \(10\text{ Hz}\) (saniyede 10) | Interpolated (Client-side Lerp)     | `NpcNetworkState` Payload |
| Eşyalar ve Reyonlar (Loot)   | \(2\text{ Hz}\) (saniyede 2)   | Yok                                  | State Events (Kırıldı/Alındı) |
| Raund Saat ve Kota (Manager) | \(1\text{ Hz}\) (saniyede 1)   | Time Compensation Drift              | RealTime Sync RPC |

---

## 4. Sunucu Arama Ağı Veri Paketi (Server Browser Payload)

Lobi listesinde veya sunucu arama arayüzünde gösterilecek olan serileştirilmiş veri paketi yapısı:

```csharp
[Serializable]
public struct ServerInfoPayload
{
    public string ServerName;       // Sunucu/Yayıncı adı
    public ulong HostSteamId;       // Kurucunun Steam ID'si
    public string LobbyCode;        // 4 haneli oda kodu (Örn: "C9X2")
    public int CurrentPlayers;      // Aktif oyuncu sayısı
    public int MaxPlayers;          // Maksimum sınır (varsayılan 4)
    public QuotaDifficulty Difficulty; // Kota zorluğu (Kolay/Orta/Zor)
    public int CurrentDayIndex;     // Kaçıncı günde olunduğu (Örn: Gün 3)
    public bool GameInProgress;     // Oyun devam ediyor mu yoksa lobi aşamasında mı?
}
```

---

## 5. Ağ Bağlantı Hataları ve Host Göçü (Host Migration Exceptions)

Multiplayer maç esnasında kurucunun hattan düşmesi veya ağ kayıplarında s&box üzerinde uygulanacak kurtarma senaryoları:

1. **Host Göç Protokolü (Host Migration Process):**
   - *Olay:* Kurucu (Host/Server) oyundan aniden çıktığında veya bağlantısı kesildiğinde.
   - *Çözüm:* İstemciler arasında en düşük P2P ping değerine sahip olan oyuncu (Master Client) yeni Host olarak seçilir. Eski sunucudan kalan en son senkronize edilmiş dünya snaphot verileri (`WorldStateSnapshot`) yeni hosta yüklenir ve sunucu rolü ona devredilerek oyun duraksamadan devam eder:
     ```csharp
     public void OnHostDisconnected()
     {
         Log.Warning("Sunucu bağlantısı koptu. Yeni host tayin ediliyor...");
         PauseGamePhysics();
         
         ulong newHostId = DetermineLowestPingClient();
         if (LocalPlayer.SteamId == newHostId)
         {
             StartHostingFromState(LastKnownWorldState);
         }
         else
         {
             ConnectToNewHost(newHostId);
         }
         ResumeGamePhysics();
     }
     ```
2. **Yüksek Paket Kaybı ve Yeniden Bağlanma (Packet Loss recovery):**
   - İstemci ile sunucu arasındaki ağ paket kaybı oranı 3 saniye boyunca %25'in üzerinde kalırsa, oyuncunun ekranında *"Bağlantı Sorunu"* uyarısı belirir. Fiziksel tahminleme tamponu (`PredictionBuffer`) genişletilerek hareketler pürüzsüzleştirilir. Eğer 10 saniye boyunca hiç paket alınamazsa oyuncu lobiden atılarak ana menüye yönlendirilir.

---

## 6. Ağ Bağlantı Zaman Aşımı ve Yeniden Deneme Politikaları (Handshake Limits & Retries)

İstemci lobiye veya oyuna bağlanmaya çalışırken kullanılacak ağ el sıkışma eşik süreleri ve yeniden bağlanma mekanizmaları:

- **Lobi Katılım Zaman Aşımı (Connection Timeout):** İstemci sunucuya bağlantı talebi gönderdikten sonra \(15.0\text{ saniye}\) içinde sunucudan `CmdJoinLobby` onayı alamazsa bağlantı iptal edilir ve *"Lobi Bağlantısı Zaman Aşımına Uğradı"* hatası gösterilir.
- **Yeniden Deneme Sıklığı (Reconnect Retries):** Bağlantı kopması durumunda istemci arka planda sunucuya yeniden bağlanmayı dener:
  - **Maksimum Deneme Sayısı:** \(5\text{ Kez}\).
  - **Deneme Aralıkları:** İlk 3 deneme \(2\text{ saniye}\) arayla, sonraki 2 deneme \(5\text{ saniye}\) arayla gerçekleştirilir.
- **Ağ El Sıkışma Durum Makinesi:**
  ```text
  [Disconnected] ──> CmdConnect ──> [Authenticating (Max 5s)] ──> [Connected]
         ▲                                                               │
         └───────────── Hata / Zaman Aşımı (15s) ────────────────────────┘
  ```


