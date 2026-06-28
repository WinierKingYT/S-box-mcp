# Çatışma, Sersemletme ve Çarpışma Fiziği Tasarım Belgesi (Ultimate CombatStunSystem)

Bu belge; robotları ve diğer oyuncuları engelleme, fırlatılabilir eşyalar, yangın söndürücü kullanımı ve araba çarpışma tork aktarımlarını tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin çatışma ve sersemletme etkilerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
public enum StunType
{
    ElectricalZap, // Robotların kısa devre olması
    PhysicalImpact, // Eşya çarpması veya araba ramming etkisi
    ChemicalBlind  // Yangın söndürücü köpüğü ile kör olma
}

/// <summary>
/// Sersemletilebilir tüm yapay zeka ve oyuncu karakterlerin uygulaması gereken arayüz.
/// </summary>
public interface IStunnable
{
    /// <summary>
    /// Karakterin stun durumunu sorgular.
    /// </summary>
    bool IsStunned { get; }

    /// <summary>
    /// Karakteri belirli bir süre ve tipte sersemletir.
    /// </summary>
    bool ApplyStun(float duration, StunType type);

    /// <summary>
    /// Sersemleme durumunu erken sonlandırır (Örn: Kaçış QTE başarısı veya takım yardımı).
    /// </summary>
    void ClearStun();
}

/// <summary>
/// Taşınabilir ve aktif olarak tetiklenebilir araçların (Yangın Söndürücü vb.) arayüzü.
/// </summary>
public interface IUsableTool
{
    /// <summary>
    /// Birincil kullanımı tetikler (LMB basılı tutma).
    /// </summary>
    void StartToolUse();

    /// <summary>
    /// Birincil kullanımı durdurur (LMB bırakma).
    /// </summary>
    void StopToolUse();

    /// <summary>
    /// Kalan kullanım miktarını (Örn: Basınç yüzdesi veya batarya şarjı) döner.
    /// </summary>
    float GetRemainingResource();
}
```

---

## 2. Arabayla Çarpma ve Kinetik Enerji Transferi Formülleri (Ramming Physics)

Alışveriş arabasının diğer oyunculara veya robotlara yüksek hızla çarpması durumunda uygulanacak fiziksel darbe ve sersemletme süreleri hesabı:

### 2.1 Kinetik Enerji Aktarım Formülü
\[E_{transfer} = \frac{1}{2} \cdot M_{cart\_total} \cdot (v_{attacker} - v_{target})^2 \cdot \cos\theta\]

Burada:
- \(M_{cart\_total}\): Çarpan arabasının kendi ağırlığı + sepetindeki malların toplam ağırlığı (\(\text{kg}\)).
- \(v_{attacker}\): Saldırgan arabanın çarpışma anındaki hız vektörünün büyüklüğü.
- \(v_{target}\): Kurbanın hızının çarpışma doğrultusundaki bileşeni.
- \(\theta\): İki arabanın çarpışma vektörleri arasındaki sapma açısı.

### 2.2 Sersemletme Süresi Hesabı
Eğer aktarılan kinetik enerji sınır eşiği olan \(E_{threshold\_stun} = 400.0\text{ Joule}\) değerini aşarsa, hedef sersemler:
\[t_{stun} = \min\left(t_{max\_stun}, \frac{E_{transfer}}{K_{stun\_damp}}\right)\]

Burada:
- \(t_{max\_stun}\): Maksimum sersemleme süresi (\(3.5\text{ saniye}\)).
- \(K_{stun\_damp}\): Enerji sönümleme sabiti (\(800.0\)).

---

## 3. Yangın Söndürücü ve Kimyasal Körlük Mekaniği (Chemical Blindness)

Çevreden veya Kara Borsa'dan edinilebilen Yangın Söndürücü aletinin çalışma prensipleri:

- **CO2 Gaz Bulutu Tetikleyicisi:** Oyuncu elindeki söndürücüyü çalıştırdığında (LMB), namlu yönünde \(3.5\text{ metre}\) menzilli ve \(30^{\circ}\) yayılım açılı konik bir gaz alanı taranır.
- **Yapay Zeka Görüş Engelleme:** Gaz bulutuna maruz kalan güvenlik robotlarının yapay zeka profillerindeki görüş parametreleri geçici olarak sıfırlanır:
  - Görüş Mesafesi (\(VisionRange\)): \(1.0\text{ metre}\).
  - Arama Durumu (`GuardState.Search`) tetiklenir ve robot yönünü şaşırarak rastgele döner.
- **Oyuncu Etkisi:** Gaz bulutunda kalan oyuncuların ekranında yoğun beyaz sis efekti belirir ve hareket hızları %30 yavaşlar.
- **Kaynak Tüketimi:** Yangın söndürücü toplam \(8.0\text{ saniye}\) sürekli kullanılabilir. Kaynak bittiğinde boş bir fiziksel nesne olarak fırlatılabilir.

---

## 4. Çatışma Görsel ve Ses Şartnamesi (VFX & SFX Cues)

Çatışma ve çarpma olaylarında s&box motorunda tetiklenecek görsel ve işitsel varlıklar:

### 4.1 Görsel Parçacık Efektleri (VFX)
- `particles/extinguisher_fog.vpcf`: Yangın söndürücü namlusundan çıkan beyaz, yoğun CO2 gaz püskürmesi.
- `particles/electric_sparks.vpcf`: Elektro-şok veya ağır darbe alan robotların gövdesinden saçılan mavi elektrik arkları.
- `particles/stun_stars.vpcf`: Sersemleyen oyuncunun başı üzerinde dönen diegetic yıldız partikülleri.

### 4.2 Ses Azaltma Tablosu (SFX Attenuation)
| Ses Olayı (Sound Event) | Min Mesafe (Tam Ses) | Max Mesafe (Sessiz) | Azaltma Eğrisi (Roll-off) | Açıklama |
|-------------------------|----------------------|---------------------|---------------------------|----------|
| `sfx_extinguisher_spray` | \(2.0\text{ metre}\) | \(12.0\text{ metre}\)| Linear | Gaz püskürtme sesi (Loop) |
| `sfx_robot_zap`          | \(3.0\text{ metre}\) | \(20.0\text{ metre}\)| Logarithmic | Robot kısa devre elektrik cızırtısı |
| `sfx_impact_heavy`       | \(4.0\text{ metre}\) | \(30.0\text{ metre}\)| Logarithmic | Arabayla yüksek hızlı çarpışma darbesi |

---

## 5. Fırlatılabilir Eşya Mekaniği ve Balistik Yörünge Hesapları (Thrown Projectiles)

Oyuncuların ellerindeki veya sepetlerindeki ağır ürünleri (Örn: Yangın Söndürücü, Mikrodalga Fırın) güvenlik robotlarına fırlatması durumunda balistik yörünge ve darbe algılama denklemleri:

### 5.1 Parabolik Yörünge Formülü
Eşya fırlatıldığında havada izleyeceği yörünge zamana (\(t\)) bağlı olarak hesaplanır:
\[\vec{x}(t) = \vec{x}_0 + \vec{v}_0 \cdot \cos\theta \cdot t\]
\[z(t) = z_0 + v_{0\_z} \cdot \sin\theta \cdot t - \frac{1}{2} \cdot g \cdot t^2\]

Burada:
- \(\vec{x}_0, z_0\): Fırlatma anındaki 3D başlangıç koordinatları.
- \(\vec{v}_0\): Fırlatma hız vektörü (LMB basılı tutma süresine bağlı olarak maksimum \(12.0\text{ m/s}\)).
- \(\theta\): Yatay fırlatma açısı.
- \(g\): Yerçekimi ivmesi (\(9.81\text{ m/s}^2\)).

### 5.2 Darbe Kuvveti ve Geri İtme (Knockback Vector)
Bir eşya hedefe çarptığında kurbana uygulanacak fiziksel geri itme hızı:
\[\vec{v}_{knockback} = \frac{\vec{F}_{impact} \cdot \Delta t}{M_{target}} + \vec{u}_{up} \cdot f_{lift}\]

Burada:
- \(\vec{F}_{impact}\): Çarpışma anındaki darbe kuvvet vektörü (\(\text{N}\)).
- \(M_{target}\): Darbeyi alan oyuncu veya robotun kütlesi (\(\text{kg}\)).
- \(f_{lift}\): Dikey olarak yukarı sıçratma sabiti (\(2.5\text{ m/s}\)).

---

## 6. Sersemleme Sıra Dışı Durumları ve Hata Yönetimi (Exception Handling)

Aksiyon esnasında gerçekleşebilecek durum geçiş çatışmaları ve çözümleri:

1. **Sürüş Esnasında Sersemleme (Stun While Driving):**
   - *Hata Durumu:* Oyuncu arabayı sürerken başka bir araba çarpar veya robot şok tabancası ateşlerse.
   - *Çözüm:* İstemci tarafında `ReleaseControl` anında çağrılır ve oyuncu arabadan ayrılarak yere savrulur. Araba ise sürtünmeyle durana kadar son fiziksel momentumuyla gitmeye devam eder.
2. **Kovalama Esnasında Robotun Sersemlemesi (Robot Stun Transition):**
   - *Hata Durumu:* Robot oyuncuyu kovalarken kafasına mikrodalga fırın fırlatılırsa.
   - *Çözüm:* Robot `GuardState.Stunned` durumuna geçer. Mevcut NavMesh hedefleri temizlenir. Sersemleme süresi (\(t_{stun}\)) bitene kadar yapay zeka davranış ağacı bloke edilir. Süre bitiminde robot `GuardState.Search` moduna geçerek son ses kaynağını kontrol eder.
3. **Ardışık Sersemletme Koruması (Stun Diminishing Returns):**
   - Robotların veya oyuncuların ardı ardına sersemletilerek süresiz olarak kilitlenmesini (stun-lock) önlemek amacıyla, her ardışık stun etkisi bir öncekinin yarı süresi kadar uygulanır:
     \[t_{stun\_effective} = t_{stun} \cdot (0.5)^{N_{consecutive}}\]

---

## 7. Stun Etkisi Veri Yapısı ve Yığınlama Kontrolü (Stun Stack Structure)

Aynı anda birden fazla kaynaktan gelen sersemleme etkilerinin yönetilmesi için kullanılacak veri modeli:

```csharp
[Serializable]
public struct ActiveStunEffect
{
    public StunType Type;
    public float StartTime;
    public float Duration;
    public string InstigatorName; // Stun uygulayan oyuncu veya nesne adı
    
    public float GetRemainingTime(float currentTime)
    {
        return Math.Max(0.0f, (StartTime + Duration) - currentTime);
    }
}

public class StunController : Component
{
    private List<ActiveStunEffect> activeStuns = new();
    private int consecutiveStunCount = 0;
    private float lastStunEndTime = 0f;

    public bool IsStunned => activeStuns.Any(s => s.GetRemainingTime(RealTime.Now) > 0f);

    public void AddStun(StunType type, float rawDuration, string instigator)
    {
        float currentTime = RealTime.Now;

        // Ardışık stun zaman aşımı kontrolü (son 5 saniye içinde başka stun bitti mi?)
        if (currentTime - lastStunEndTime < 5.0f)
        {
            consecutiveStunCount++;
        }
        else
        {
            consecutiveStunCount = 0;
        }

        // Diminishing returns (azalan verimler) çarpanı hesapla
        float multiplier = (float)Math.Pow(0.5, consecutiveStunCount);
        float effectiveDuration = rawDuration * multiplier;

        if (effectiveDuration < 0.2f) return; // 0.2 saniyenin altındaki stunları es geç

        var newStun = new ActiveStunEffect
        {
            Type = type,
            StartTime = currentTime,
            Duration = effectiveDuration,
            InstigatorName = instigator
        };

        activeStuns.Add(newStun);
        lastStunEndTime = currentTime + effectiveDuration;
        
        // Sersemleme animasyonunu ve sesini başlat
        TriggerStunVisuals(type, effectiveDuration);
    }

    public void Update()
    {
        // Süresi biten stunları temizle
        activeStuns.RemoveAll(s => s.GetRemainingTime(RealTime.Now) <= 0f);
    }
}
```


