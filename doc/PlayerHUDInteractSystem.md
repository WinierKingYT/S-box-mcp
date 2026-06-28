# Arayüz, Etkileşim ve Dinamik Radar/Outline Tasarım Belgesi (Ultimate PlayerHUDInteractSystem)

Bu belge; oyuncu ekranında reyon ürünlerinin vurgulanması, etkileşim göstergeleri, dinamik 2D mini-harita (radar) çizimleri ve s&box UI bileşen arayüzlerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin arayüz ve etkileşim algılayıcılarını kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
public enum RadarBlipType
{
    LocalPlayer,
    Teammate,
    GuardRobot,
    BlackMarket,
    CashierRegister,
    PingIndicator
}

/// <summary>
/// Mini-haritada/Radarda görünmesi gereken tüm dinamik varlıkların uygulayacağı arayüz.
/// </summary>
public interface IRadarDetectable
{
    /// <summary>
    /// Varlığın dünyadaki anlık 3D koordinatını döner.
    /// </summary>
    Vector3 GetWorldPosition();

    /// <summary>
    /// Mini-haritada hangi simge/renkle çizileceğini belirler.
    /// </summary>
    RadarBlipType GetBlipType();

    /// <summary>
    /// Varlığın radarda aktif olarak görünür olup olmadığını sorgular (Örn: Görüş dışında kalan robotların gizlenmesi).
    /// </summary>
    bool IsVisibleOnRadar(Vector3 observerPosition, float radarRange);
}

/// <summary>
/// Ekranda çizdirilen etkileşim (E tuşu prompt) sistemini yöneten arayüz.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Etkileşim menzilini döner.
    /// </summary>
    float InteractionRange { get; }

    /// <summary>
    /// Oyuncu etkileşime geçtiğinde tetiklenecek metot.
    /// </summary>
    void OnInteract(GameObject instigator);

    /// <summary>
    /// Ekranda belirecek diegetic metni döner (Örn: "Mücevher Al [E]").
    /// </summary>
    string GetInteractionPrompt();
}
```

---

## 2. Post-Process Eşya Silueti Vurgulama Kuralları (Highlight Outlines)

Oyuncuların reyonlar arasında hızla hareket ederken malları fark edebilmesi için s&box Post-Processing render hattında uygulanacak outline renk kodları ve mesafe kuralları:

- **Algılama Menzili:** Oyuncu kamerasına olan uzaklığı \(\le 3.0\text{ metre}\) ve bakış açısıyla (FOV) \(\le 45^{\circ}\) uyumlu olan tüm eşyalar parlatılır.
- **Renk Derecelendirmeleri (RGB Codes):**
  - **Sıradan Ürünler (Medium/Normal):** Yeşil Outline (`#00FF33` - R:0, G:255, B:51).
  - **Kırılgan Ürünler (Fragile):** Turkuaz Outline (`#00E5FF` - R:0, G:229, B:255).
  - **Lüks/Kota Ürünleri (Luxe):** Altın Sarısı Outline (`#FFD700` - R:255, G:215, B:0).
  - **Kritik Araçlar/Yükseltmeler (Extinguisher/Nitro vb.):** Turuncu Outline (`#FF6D00` - R:255, G:109, B:0).

---

## 3. Tohum Tabanlı Dinamik Mini-Harita ve Radar Çizim Mantığı (Radar Blips)

AVM procedural üretildiği için statik bir harita resmi yerine seed-based grid verisi kullanılarak dinamik çizim yapılır:

### 3.1 Mini-Harita Dönüşüm Formülü
Dünyadaki 3D koordinatı (\(P_{world}\)), 2D mini-harita UI piksel koordinatına (\(P_{ui}\)) dönüştürme formülü:
\[P_{ui}.x = \text{Center}_{ui}.x + (P_{world}.x - P_{player}.x) \cdot S_{scale} \cdot \cos\psi - (P_{world}.y - P_{player}.y) \cdot S_{scale} \cdot \sin\psi\]
\[P_{ui}.y = \text{Center}_{ui}.y + (P_{world}.x - P_{player}.x) \cdot S_{scale} \cdot \sin\psi + (P_{world}.y - P_{player}.y) \cdot S_{scale} \cdot \cos\psi\]

Burada:
- \(P_{player}\): Kamerayı kontrol eden oyuncunun anlık konumu.
- \(\psi\): Oyuncunun yatay bakış yönü açısı (Yaw).
- \(S_{scale}\): Harita ölçek çarpanı (Örn: \(\frac{1}{10}\) piksel/metre).
- \(\text{Center}_{ui}\): Mini-harita panelinin ekran üzerindeki merkez noktası (Örn: \(120, 120\)).

### 3.2 Radar Görünürlük Kısıtları (Fog of War)
- Robotlar sadece oyuncunun görüş alanındayken veya gürültü çıkardıklarında radarda kırmızı nokta olarak belirir.
- Gürültü bittiğinde kırmızı nokta radarda son göründüğü yerde 3 saniye boyunca soluklaşarak (fade-out) kalır ve sonra tamamen silinir.

---

## 4. Teammate Ping Sistemi ve 3D Dünya Anchors (Ping System)

Oyuncuların yardımlaşmasını kolaylaştırmak için geliştirilen, dünyadaki ve mini-haritadaki dinamik işaretleme (Ping) mekaniği:

- **Tetikleyici Girdi (Input Action):** Oyuncu `[MMB] (Orta Tuş)` bastığında, kameranın baktığı yönde \(15.0\text{ metre}\) menzilli bir Raycast fırlatılır.
- **Darbe Tespiti:**
  - Eger Raycast bir LootItem ile kesişirse: Eşya üzerinde turuncu bir dairesel 3D gösterge (world-space anchor) belirir ve radarda ping ikonu yanıp söner. Sohbet kutusuna otomatik mesaj yazılır: *"Loot İşaretlendi: [Eşya Adı] ($[Fiyat])"*.
  - Eger Raycast bir Güvenlik Robotu ile kesişirse: Robot kırmızı bir ünlem işareti ile işaretlenir ve radardaki kırmızı blip simgesi 5 saniye boyunca sabit görünür kalır.
- **Sönümlenme Süresi:** Tüm aktif ping işaretçileri 6 saniye sonra otomatik olarak yok edilir.

---

## 5. Üst Üste Binen Etkileşimlerin Çözülmesi (Interaction Overlap Priority Resolver)

Dar reyonlarda birden fazla eşya yan yana durduğunda etkileşim hedefinin kafa karıştırmasını engellemek için kullanılan sıralama algoritması:

### 5.1 Öncelik Skoru Formülü
\[S_{priority} = \frac{\cos\alpha}{d^2} \cdot W_{type}\]

Burada:
- \(d\): Oyuncunun kamerası ile eşya merkezi arasındaki mesafe.
- \(\alpha\): Kameranın bakış yönü vektörü ile eşyaya doğru giden vektör arasındaki açı.
- \(W_{type}\): Varlık türü ağırlık katsayısı:
  - Görev/Kota Hedefi Eşya: \(W_{type} = 2.5\)
  - Nezarethane Hücre Kapısı / Asansör: \(W_{type} = 3.0\)
  - Yangın Söndürücü / Araçlar: \(W_{type} = 2.0\)
  - Sıradan Loot Eşyası: \(W_{type} = 1.0\)

*Sistem her karede etkileşim alanındaki tüm nesneler için \(S_{priority}\) skorunu hesaplar ve en yüksek skora sahip olan tek bir nesneyi aktif etkileşim hedefi (`ActiveInteractable`) seçer. Diğer eşyaların outline parlaması kapatılır.*

---

## 6. s&box Panel Arayüz Hiyerarşisi (HUD UI Layout)

```text
RootPanel (Full Screen)
 ├── MatchTimerLabel (Top Center) - Raund Kalan Süresi
 ├── TeamQuotaStatusPanel (Top Left) - Kota Barı ve Yatırılan Para
 ├── MiniMapPanel (Top Right) - 2D Radar Daire Tuvali
 │    └── RadarBlipsContainer (Dinamik simge katmanı)
 ├── InteractionPromptPanel (Center Screen) - Ekrana Yönelik 3D Prompts
 └── PocketInventoryPanel (Bottom Center) - 3 Cep Slotu Görseli
```

---

## 7. Post-Process Vurgulama Materyal Şartnamesi (Shader Parameters)

Vurgulanan eşyaların kenar çizgisi (Outline) s&box render hattında özel parametrelerle çalışır:
- **Kenar Kalınlığı (Outline Width):** \(2.0\text{ piksel}\) (Görünürlüğü bozmayacak derecede ince).
- **Parıltı Dalgası (Glow Pulse Frequency):** Lüks/Kota eşyalarında neon parıltı hissi vermek için altın sarısı renk \(1.5\text{ Hz}\) frekansta salınım yapar:
  \[I_{current} = I_{base} \cdot (1.0 + 0.3 \cdot \sin(\text{Time} \cdot 2\pi \cdot 1.5))\]
- **Engellerin Arkasından Görünüm (Occlusion rendering):** Eşyalar duvar arkasında kaldığında outline silueti %30 opaklıkla mavi renkte çizilerek yerleri sezilebilir hale getirilir.

---

## 8. C# Etkileşim Algılayıcı Sınıf Yapısı (InteractionController Class)

Eşya algılama ve öncelik sıralamasını koordine eden ana C# kontrol bileşeni:

```csharp
public class InteractionController : Component
{
    [Property] public float SearchRadius { get; set; } = 3.0f;
    public IInteractable ActiveTarget { get; private set; }

    public void Update()
    {
        var scanCenter = Transform.Position + Transform.Rotation.Forward * 1.0f;
        var overlaps = Scene.FindInPhysics<IInteractable>(new Sphere(scanCenter, SearchRadius));

        IInteractable bestTarget = null;
        float bestScore = -1f;

        foreach (var interactable in overlaps)
        {
            float distance = Vector3.Distance(Transform.Position, interactable.Transform.Position);
            if (distance > interactable.InteractionRange) continue;

            Vector3 directionToTarget = (interactable.Transform.Position - Transform.Position).Normal;
            float cosAngle = Vector3.Dot(Transform.Rotation.Forward, directionToTarget);

            if (cosAngle < 0.707f) continue; // 45 derecelik görüş konisi dışındakileri ele

            // Öncelik katsayısı
            float wType = GetTypeWeight(interactable);
            float score = (cosAngle / (distance * distance)) * wType;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = interactable;
            }
        }

        UpdateActiveTarget(bestTarget);
    }

    private void UpdateActiveTarget(IInteractable newTarget)
    {
        if (ActiveTarget == newTarget) return;

        // Eski hedefi kapat
        if (ActiveTarget is Component oldComp && oldComp.IsValid)
        {
            ToggleOutline(oldComp, false);
        }

        ActiveTarget = newTarget;

        // Yeni hedefi aktifleştir
        if (ActiveTarget is Component newComp && newComp.IsValid)
        {
            ToggleOutline(newComp, true);
        }
    }

    private float GetTypeWeight(IInteractable target)
    {
        if (target.GameObject.Tags.Has("quota_item")) return 2.5f;
        if (target.GameObject.Tags.Has("utility_tool")) return 2.0f;
        return 1.0f;
    }

    private void ToggleOutline(Component comp, bool state)
    {
        if (comp.GameObject.Components.TryGet<ModelRenderer>(out var renderer))
        {
            renderer.Attributes.Set("outline_enabled", state ? 1 : 0);
        }
    }
}
```
