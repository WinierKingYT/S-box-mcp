# Akustik Ses Yayılımı ve Yapay Zeka Algılama Tasarım Belgesi (Ultimate AcousticsPropagationSystem)

Bu belge; gürültülerin reyon duvarları ve koridorlar boyunca nasıl yayıldığını, engel zayıflatma çarpanlarını ve robotların ses algılama eşiklerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin akustik yayılım ve ses dinleyici sistemlerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
[Serializable]
public struct NoiseEvent
{
    public Vector3 OriginPosition; // Sesin çıktığı nokta
    public float Decibels;         // Başlangıç ses seviyesi (dB)
    public string SoundName;       // Ses varlığı adı
    public GameObject Instigator;  // Sesi çıkaran varlık (Oyuncu, Araba vb.)
}

/// <summary>
/// Gürültü yayabilen nesnelerin uyması gereken arayüz.
/// </summary>
public interface INoiseEmitter
{
    /// <summary>
    /// Dünyada belirli bir desibel seviyesinde ses yayar.
    /// </summary>
    void EmitNoise(float decibelLevel, string soundName);
}

/// <summary>
/// Çevredeki sesleri duyabilen nesnelerin (Güvenlik Robotu vb.) uygulayacağı arayüz.
/// </summary>
public interface INoiseListener
{
    /// <summary>
    /// Akustik sisteme kayıtlı olan nesnenin ses algılama eşiğini döner (dB).
    /// </summary>
    float HearingThresholdDb { get; }

    /// <summary>
    /// Akustik sistem tarafından sönümlenmiş bir ses dalgası ulaştığında tetiklenir.
    /// </summary>
    void OnNoiseHeard(NoiseEvent noise);
}
```

---

## 2. Akustik Desibel Sönümlenme ve Duvar Engel Engelleme Formülleri

Sesin AVM içinde düz bir çizgide (Euclidean) ilerlemek yerine, koridorları takip ederek ve reyon duvarlarından sekerek yayıldığını hesaplayan akustik model:

### 2.1 Desibel Azalma Formülü (Acoustics Propagation Model)
\[L_{target} = L_{source} - 20 \cdot \log_{10}(d_{path}) - N_{walls} \cdot \alpha_{wall}\]

Burada:
- \(L_{target}\): Dinleyicinin (Robotun) kulağına ulaşan ses seviyesi (\(\text{dB}\)).
- \(L_{source}\): Kaynağın yaydığı başlangıç desibel seviyesi (\(\text{dB}\)).
- \(d_{path}\): Harita gridleri üzerinden hesaplanan en kısa fiziksel yol mesafesi (\(\text{metre}\)).
- \(N_{walls}\): Sesin kaynağından dinleyiciye giden doğrultuda kesiştiği dükkan bölme duvarı veya kapalı kepenk sayısı.
- \(\alpha_{wall}\): Duvar başına ses yutma katsayısı (\(20\text{ dB}\)).

### 2.2 Çeşitli Eylemlerin Ses Gücü Kütüphanesi
- `sfx_glass_rattle` (Sepette cam tıkırtısı): \(60\text{ dB}\).
- `sfx_metal_rattle` (Sepette metal tıkırtısı): \(65\text{ dB}\).
- `sfx_extinguisher_spray` (Söndürücü sıkma): \(75\text{ dB}\).
- `sfx_shelf_crash` (Reyon devrilmesi): \(95\text{ dB}\).
- `sfx_tire_screech` (Drift sesi): \(80\text{ dB}\).

---

## 3. Yapay Zeka Ses Algılama Algoritması (Grid-Based Propagation Search)

Yapay zeka robotlarının bir gürültüyü duyup duymadığını doğrulamak amacıyla kullanılan grid tabanlı arama ve filtreleme pseudo-kodu:

```
FONKSİYON CalculateAcousticPath(noiseOrigin, listenerPos, maxRadius)
    // 1. Kaba uzaklık testi (Euclidean) - Eger doğrudan mesafe çok uzaksa hesaplamayı es geç
    Mesafe = Vector3.Distance(noiseOrigin, listenerPos)
    EGER Mesafe > maxRadius:
        DÖNÜŞ YANLIS // Ses robotun menzili dışındadır
    SONRA

    // 2. Grid koordinatlarını bul
    NoiseCell = GetGridCellFromWorldPos(noiseOrigin)
    ListenerCell = GetGridCellFromWorldPos(listenerPos)

    // 3. Grid üzerinde BFS/A* ile en kısa koridor yolunu bul
    YolMesafesi = PathFinder.GetGridDistance(NoiseCell, ListenerCell) // metre cinsinden yol

    // 4. Araya giren duvar sayısını doğrultu vektörü (Raycast) ile hesapla
    DuvarSayisi = RaycastIntersectionCount(noiseOrigin, listenerPos, Layer.Wall)

    // 5. Nihai ulasan desibeli hesapla
    UlasanDb = NoiseSourceDb - 20 * Log10(YolMesafesi) - (DuvarSayisi * 20)

    EGER UlasanDb >= Listener.HearingThresholdDb:
        DÖNÜŞ DOGRU (UlasanDb) // Robot sesi duydu!
    YOKSA
        DÖNÜŞ YANLIS
    SONRA
SONFONKSİYON
```

---

## 4. Malzeme Tabanlı Ses Engel Yutma Katsayıları (Obstruction Material Registry)

Ses yollarının kesiştiği AVM mimari malzemelerinin türlerine göre desibel (\(\text{dB}\)) yutma oranları:

| Engel Malzeme Türü (Obstacle Material) | Yutma Katsayısı (\(\alpha_{obstacle}\)) | Açıklama / Oyun Etkisi |
|----------------------------------------|-----------------------------------------|------------------------|
| Alçıpan Bölme Duvar (Plasterboard)    | \(15.0\text{ dB}\)                      | Standart dükkan sınırları |
| Beton Taşıyıcı Kolon (Reinforced)     | \(40.0\text{ dB}\)                      | Ağır strüktür, sesi neredeyse tamamen keser |
| Cam Reyon Vitrini (Glass Partition)    | \(5.0\text{ dB}\)                       | İnce vitrin camları, sesi rahatça iletir |
| Metal Kepenk (Security Grate)          | \(30.0\text{ dB}\)                      | Kapalı durumdaki dükkan metal kepenkleri |

---

## 5. Akustik Performans Güvenlik Sınırları (Performance & Optimization Limits)

Dinamik yol arama ve Raycast sorgularının s&box sunucu performansını baltalamasını önlemek amacıyla getirilen kısıtlamalar:

1. **Arama Menzil Limiti (Max Raycast Bounds):**
   Mesafe \(\ge 25\text{ metre}\) olan ses yayılımları için grid/akustik yörünge analizi yapılmaz; doğrudan ses sönümlenmiş kabul edilir.
2. **Kesişim Limiti (Max Obstruction Raycasts):**
   Tek bir ses yayılım analizinde en fazla \(6\) engel kesişimi (Raycast hits) işlenir. Altıncı engelden sonra ses seviyesi sıfır kabul edilir.
3. **Önbelleğe Alma (Acoustic Path Caching):**
   Aynı hücre içindeki benzer gürültüler için son 0.5 saniyede hesaplanan en kısa yollar (`d_path`) önbellekten çekilir, NavMesh güncellenene kadar tekrar BFS çalıştırılmaz.

---

## 6. Katlar Arası Dikey Ses Geçişleri (Vertical Multi-Floor Acoustics)

Katlar arasındaki merdiven boşlukları, tahliye asansörleri ve açık galeri boşlukları boyunca sesin dikey sönümlenme kuralları:

### 6.1 Dikey Kat Sönümlenme Formülü
Kat zeminleri betonarme tavanlar olduğu için ses dikey olarak keskin bir sönümlenmeye maruz kalır. Sesin bir kattan diğerine geçiş gücü şu formülle hesaplanır:
\[L_{target} = L_{source} - 20 \cdot \log_{10}(d_{path}) - N_{floors} \cdot \alpha_{floor} - \beta_{shaft}\]

Burada:
- \(N_{floors}\): Ses kaynağı ile dinleyici arasındaki kat farkı.
- \(\alpha_{floor}\): Katlar arası yalıtım sönümleme sabiti (\(35.0\text{ dB}\)).
- \(\beta_{shaft}\): Sesin geçtiği dikey galeri boşluğuna bağlı kolaylaştırıcı ofset değeri:
  - Ses doğrudan açık asansör boşluğundan geçiyorsa: \(\beta_{shaft} = 10\text{ dB}\) (ofset zayıflamayı azaltır; ses daha gür ulaşır).
  - Ses kapalı tavan/zemin üzerinden geçmeye çalışıyorsa: \(\beta_{shaft} = 60\text{ dB}\) (ses neredeyse tamamen engellenir).

### 6.2 Dikey Geçiş Algoritma Akışı
```csharp
public float CalculateVerticalDecay(Vector3 sourcePos, Vector3 listenerPos, float initialDb)
{
    int sourceFloor = GetFloorFromHeight(sourcePos.z);
    int listenerFloor = GetFloorFromHeight(listenerPos.z);
    int floorDifference = Math.Abs(sourceFloor - listenerFloor);

    if (floorDifference == 0) return 0f; // Aynı kattalar, dikey sönümleme yok

    // Yakındaki açık asansör veya merdiven boşluğu kontrolü
    Vector3 nearestShaft = GetNearestAcousticShaft(sourcePos);
    float distanceToShaft = Vector3.Distance(sourcePos, nearestShaft);
    float distanceFromShaftToListener = Vector3.Distance(nearestShaft, listenerPos);
    float totalPathDistance = distanceToShaft + distanceFromShaftToListener;

    float floorDecay = floorDifference * 35.0f;
    float shaftOffset = 10.0f; // Açık boşluk kolaylaştırması

    float totalDecay = (20f * (float)Math.Log10(totalPathDistance)) + floorDecay - shaftOffset;
    return totalDecay;
}
```


