# Prosedürel Harita ve Dikey İlerleme Sistemi Tasarım Belgesi (Ultimate MallArchitectureSystem)

Bu belge; C# prosedürel harita ve NavMesh API arayüzlerini ve harita çevre/yıkım seslerinin 3D uzamsal sönümlenme parametrelerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin prosedürel harita üretimini ve dinamik yapay zeka yollarını kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
/// <summary>
/// Grid tabanlı prosedürel harita üretimini yöneten arayüz.
/// </summary>
public interface IMallGenerator
{
    /// <summary>
    /// Belirtilen sayısal tohumu (seed) kullanarak harita layout'unu üretir.
    /// </summary>
    void GenerateLayout(int mapSeed);

    /// <summary>
    /// Haritada yer alan tüm reyonlardaki malların toplam değerini hesaplar.
    /// </summary>
    float CalculateTotalLootValue();

    /// <summary>
    /// BFS algoritması kullanarak kasa ve başlangıç noktaları arası yol bağlantısını doğrular.
    /// </summary>
    bool ValidatePathConnection();
}

/// <summary>
/// Yıkılan engeller sonrası NavMesh yollarını dinamik güncelleyen arayüz.
/// </summary>
public interface INavMeshUpdater
{
    /// <summary>
    /// Belirtilen merkez konum etrafındaki lokal NavMesh alanını baştan inşa eder.
    /// </summary>
    void RebuildLocalBounds(Vector3 centerPoint, float radius);

    /// <summary>
    /// Sıkışan yapay zeka karakterlerin NavMesh üzerindeki takılmalarını çözer.
    /// </summary>
    void ResolveAgentStuck(GameObject agentObject);
}
```

---

## 2. Harita Çevre Sesleri Varlık Şartnamesi (Audio Attenuation)

Reyon raflarının kırılması, kolonların yıkılması ve asansör varış anlarında çalacak olan seslerin 3D uzamsal sönümlenme parametreleri:

| Ses Olayı (Sound Event) | Min Mesafe (Tam Ses) | Max Mesafe (Sessiz) | Azaltma Eğrisi (Roll-off) | Döngü (Loop) |
|-------------------------|----------------------|---------------------|---------------------------|--------------|
| `sfx_shelf_crash` (Raf Kırılması) | \(5.0\text{ metre}\) | \(30.0\text{ metre}\)| Logarithmic | Hayır |
| `sfx_wall_dust` (Kolon Yıkılması) | \(5.0\text{ metre}\) | \(35.0\text{ metre}\)| Logarithmic | Hayır |
| `sfx_elevator_bell` (Asansör Zili) | \(3.0\text{ metre}\) | \(18.0\text{ metre}\)| Linear | Hayır |
| `sfx_grate_opening` (Kepenk Açılışı)| \(2.0\text{ metre}\) | \(15.0\text{ metre}\)| Logarithmic | Hayır |

---

## 3. Prosedürel Grid ve BFS Yol Doğrulama Algoritması (Procedural Generation & Validation)

AVM'nin her oyunda prosedürel üretilmesi sonrasında, kritik noktaların birbirine bağlı olduğunu ve tıkalı yollar kalmadığını doğrulayan BFS (Breadth-First Search) algoritması:

### 3.1 Harita Hücre Tipleri (Cell Types Enum)
```csharp
public enum CellType
{
    Wall,
    Corridor,
    ShopNormal,
    ShopLuxe,
    CashierRegister,
    JailRegion,
    BlackMarketRegion,
    SpawnPoint
}
```

### 3.2 BFS Bağlantı Doğrulama Pseudo-Kodu
```
FONKSİYON ValidatePathConnection(grid2D[16][16], startPos)
    // Başlangıç noktasından (SpawnPoint) Kasa, Nezarethane ve Kara Borsa'ya ulaşılabildiğini test et.
    KritikHedefler = Liste(CellType.CashierRegister, CellType.JailRegion, CellType.BlackMarketRegion)
    BulunanHedefler = BosKume()
    
    ZiyaretEdilenler = booleanGrid[16][16] (tamamı YANLIS)
    Kuyruk = BosKuyruk()
    
    Kuyruk.Ekle(startPos)
    ZiyaretEdilenler[startPos.X][startPos.Y] = DOGRU
    
    Kuyruk BosOlmadigiSurece:
        Mevcut = Kuyruk.Cikar()
        
        // Eger kritik bir hedef hücresindeysek kümeye ekle
        EGER grid2D[Mevcut.X][Mevcut.Y] icinde var ise KritikHedefler:
            BulunanHedefler.Ekle(grid2D[Mevcut.X][Mevcut.Y])
        SONRA
        
        // 4 Yönlü Komşuları Gez (Kuzey, Güney, Doğu, Batı)
        Komsular = Liste(
            Point(Mevcut.X + 1, Mevcut.Y),
            Point(Mevcut.X - 1, Mevcut.Y),
            Point(Mevcut.X, Mevcut.Y + 1),
            Point(Mevcut.X, Mevcut.Y - 1)
        )
        
        HER bir Komsu icindeki Komsular:
            EGER Komsu.X >= 0 VE Komsu.X < 16 VE Komsu.Y >= 0 VE Komsu.Y < 16:
                EGER ZiyaretEdilenler[Komsu.X][Komsu.Y] == YANLIS VE grid2D[Komsu.X][Komsu.Y] != CellType.Wall:
                    ZiyaretEdilenler[Komsu.X][Komsu.Y] = DOGRU
                    Kuyruk.Ekle(Komsu)
                SONRA
            SONRA
        SONHER
    SONKUYRUK
    
    // Tüm kritik hedeflere ulaşılabildi mi?
    EGER BulunanHedefler.ElemanSayisi == KritikHedefler.ElemanSayisi:
        DOGRU doner // Harita oynanabilir!
    YOKSA
        YANLIS doner // Haritayı yeniden üret (Seed değiştir)
    SONRA
SONFONKSİYON
```

---

## 4. Modüler Hücre Yerleşimi ve Bağlantı Şeması (ASCII Layout)

Her bir grid hücresinin (10m x 10m) iç yapısı ve komşu hücrelerle olan bağlantı yolları şematik görünümü:

```text
  [ KORİDOR HÜCRESİ ]             [ MAĞAZA HÜCRESİ ]
+-------------------+           +-----+  KAPI  -----+
|     YOL ALANI     |           |  [R]  [R]  [R]    |  <- [R] Reyon Rafı
|                   |           |  [R]  [R]  [R]    |  (Yıkılabilir)
|  <--- GEÇİŞ --->  |           |                   |
|                   |           |     KORİDOR       |
|     YOL ALANI     |           |     BOŞLUĞU       |
+-------------------+           +-------------------+
```

---

## 5. Dinamik NavMesh Güncelleme Akışı (Localized NavMesh Rebuild)

Alışveriş merkezindeki rafların veya kolonların çarpışma sonucu yıkılmasıyla yolların açılması veya kapanması durumunda NavMesh motorunun çalışma prensipleri:

1. **Lokal Sınır Tespiti (Bounding Box):**
   Yıkım gerçekleştiğinde, sunucu olay merkezinde \(16\text{m} \times 16\text{m} \times 8\text{m}\) boyutlarında bir lokal AABB (Axis-Aligned Bounding Box) oluşturulur.
2. **Asenkron Yeniden Yapılandırma (Async Rebuild):**
   Sunucu CPU kilitlenmesini engellemek için `INavMeshUpdater.RebuildLocalBounds` asenkron bir thread üzerinde çalıştırılır:
   ```csharp
   public async Task RebuildLocalBoundsAsync(Vector3 centerPoint, float radius)
   {
       var bounds = new BBox(centerPoint - new Vector3(radius), centerPoint + new Vector3(radius));
       // s&box Engine NavMesh asenkron update çağrısı
       await NavMesh.UpdateBoundsAsync(bounds);
       
       // Etraftaki tüm aktif NPC'lerin hedeflerini yeniden değerlendir
       NotifyAllAIOnPathChanged(centerPoint, radius);
   }
   ```
3. **Yapay Zeka Sinyali (AI Re-pathing Trigger):**
   NavMesh yenilendiğinde, o alandan geçmeye çalışan veya o alana hedefi bulunan devriye robotları `ResolveAgentStuck` fonksiyonu ile mevcut yollarını iptal edip anlık olarak tekrar rota hesaplarlar.
