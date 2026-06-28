# Envanter ve Taşıma Sistemi Tasarım Belgesi (Ultimate InventorySystem)

Bu belge; C# envanter API arayüzlerini, sepet sanal envanter slot yükseltme yollarını ve envanter seslerinin 3D uzamsal sönümlenme parametrelerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin sepet, cep ve el envanter sistemlerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
/// <summary>
/// Tüm envanter taşıyıcılarının (Sepet, Cep, El) uyması gereken temel arayüz.
/// </summary>
public interface IInventory
{
    /// <summary>
    /// Envantere yeni bir LootItem ekler. Kapasite aşılırsa false döner.
    /// </summary>
    bool AddItem(LootItem item);

    /// <summary>
    /// Belirtilen eşyayı envanterden çıkarır ve fiziksel olarak serbest bırakır.
    /// </summary>
    bool RemoveItem(LootItem item);

    /// <summary>
    /// Envanterdeki tüm kayıtlı ürünlerin toplam USD değerini hesaplar.
    /// </summary>
    float CalculateTotalValue();

    /// <summary>
    /// Envanterdeki tüm kayıtlı ürünlerin toplam kütlesini (kg) hesaplar.
    /// </summary>
    float GetTotalMass();
}

/// <summary>
/// Oyuncunun eliyle tekil eşya taşıma ve sürükleme durumlarını yöneten arayüz.
/// </summary>
public interface IItemCarrier
{
    /// <summary>
    /// Oyuncunun eline bir eşya yerleştirir.
    /// </summary>
    bool CarryInHand(LootItem item);

    /// <summary>
    /// Eldeki eşyayı serbest bırakır (yere atar veya sepete bırakır).
    /// </summary>
    LootItem ReleaseHandItem();

    /// <summary>
    /// Oyuncunun ellerinin dolu olup olmadığını kontrol eder.
    /// </summary>
    bool AreHandsFull { get; }
}
```

---

## 2. Çanta/Sepet Seviye Yükseltme Yolu (Slot Tiers)

Alışveriş sepetinin sanal envanter slot kapasitesinin kalıcı AVM Sadakat Puanı (Cashback) ile yükseltilebilen aşamaları:

- **Tier 1 (Standart Sepet):** 
  - Maliyet: 0 Puan (Başlangıç).
  - Sanal Slot Sınırı: **4 Slot**.
  - Ağırlık Limit Cezası: Yok (Standart sürüş etkileri geçerlidir).
- **Tier 2 (Genişletilmiş Sepet):** 
  - Maliyet: 40 Puan.
  - Sanal Slot Sınırı: **6 Slot**.
  - Fiziksel Etki: Sepet hacmi görsel olarak genişler, trigger box boyutları Y ve Z eksenlerinde %15 büyütülür.
- **Tier 3 (Mega Kargo Sepeti):** 
  - Maliyet: 80 Puan.
  - Sanal Slot Sınırı: **8 Slot**.
  - Fiziksel Etki: Trigger box boyutları Y ve Z eksenlerinde %30 büyütülür. Sürüş ataleti (`LinearDrag`) doluyken %5 azaltılır (daha iyi kayma performansı).

---

## 3. Envanter Ses Varlık Şartnamesi (Audio Attenuation)

Envanter hareketlerinin, cep şıngırtılarının ve dökülmelerinin 3D uzamsal ses (Spatial Audio) sönümlenme parametreleri:

| Ses Olayı (Sound Event) | Min Mesafe (Tam Ses) | Max Mesafe (Sessiz) | Azaltma Eğrisi (Roll-off) | Döngü (Loop) |
|-------------------------|----------------------|---------------------|---------------------------|--------------|
| `sfx_glass_rattle` (Cam Şıngırtısı) | \(0.5\text{ metre}\) | \(6.0\text{ metre}\) | Logarithmic | Evet (Hıza bağlı pitch) |
| `sfx_metal_rattle` (Metal Şıngırtısı) | \(0.5\text{ metre}\) | \(6.0\text{ metre}\) | Logarithmic | Evet (Hıza bağlı pitch) |
| `sfx_spillage_impact` (Dökülme Sesi) | \(3.0\text{ metre}\) | \(25.0\text{ metre}\)| Logarithmic | Hayır |
| `sfx_item_register_beep` (Bip Sesi)  | \(1.0\text{ metre}\) | \(10.0\text{ metre}\)| Linear | Hayır |

---

## 4. LootItem Veri Yapısı (C# Struct & Enums)

Eşyaların fiziksel ve ekonomik niteliklerini belirleyen veri yapıları:

```csharp
public enum ItemSize
{
    Small,  // Cebe sığabilir (Kupon, Çikolata, Parfüm vb.)
    Medium, // Sadece sepete sığabilir (Mikrodalga fırın, Kıyafet yığını)
    Large   // Sadece tek elle taşınabilir (Buzdolabı, Büyük Ekran TV)
}

[Serializable]
public struct LootItem
{
    public string ItemId;
    public string ItemName;
    public ItemSize Size;
    public float BaseValue;      // Ham USD değeri
    public float WeightKg;       // Fiziksel ağırlık
    public bool IsFragile;       // Kırılgan mı? (Düşerse değeri sıfırlanır)
    public float FrictionFactor; // Sepet içi sürtünme katsayısı
    public float SpillChanceWeight; // Çarpışmada fırlama olasılık katsayısı
}
```

---

## 5. Fiziksel Dökülme ve Fırlama Algoritması (Spillage Dynamics)

Çarpışma anında sepetten eşya dökülme ihtimalini ve fırlama vektörünü belirleyen matematiksel hesaplamalar ve pseudo-kod:

### 5.1 Dökülme Olasılığı Formülü
\[P_{spill} = \min\left(1.0, \frac{\max(0.0, v_{impact} - v_{thresh}) \cdot M_{total} \cdot (1.0 - C_{protect})}{K_{stability}}\right)\]

Burada:
- \(v_{impact}\): Çarpışma anındaki hız vektörünün büyüklüğü (\(\text{m/s}\))
- \(v_{thresh}\): Dökülmeyi tetikleyen minimum eşik hız (\(3.0\text{ m/s}\))
- \(M_{total}\): Sepetteki aktif ürünlerin toplam ağırlığı (\(\text{kg}\))
- \(C_{protect}\): Ön tamponun dökülme koruma çarpanı (Tier 1 için \(0.5\), Tier 2 için \(0.8\), Tier 3 için \(1.0\))
- \(K_{stability}\): Sepet stabilite sabiti (\(150.0\))

### 5.2 Fırlama Kuvveti ve Yönü Vektörü
Fırlayan ürünlerin sepetten dışarı doğru saçılma yönü (\(\vec{F}_{eject}\)) ve açısı:
\[\vec{F}_{eject} = \text{Rotation}(0, \theta_{rand}, 0) \cdot \left( -\vec{v}_{impact} \cdot \gamma_{rebound} + \vec{u}_{up} \cdot f_{upward} \right) \cdot \frac{1.0}{WeightKg}\]

Burada:
- \(\theta_{rand}\): \([-30^{\circ}, 30^{\circ}]\) arası rastgele yatay sapma açısı.
- \(\gamma_{rebound}\): Çarpışma geri tepme esneklik çarpanı (\(0.4\)).
- \(\vec{u}_{up}\): Yukarı yönlü birim vektör.
- \(f_{upward}\): Yukarı fırlatma dikey kuvvet sabiti (\(5.0\)).

### 5.3 C# Dökülme Kontrol Pseudo-Kodu
```csharp
public void OnCollisionDetected(Vector3 relativeVelocity, Vector3 collisionNormal)
{
    float vImpact = relativeVelocity.Magnitude;
    if (vImpact < 3.0f) return;

    float totalMass = GetTotalMass();
    float protection = BumperUpgrade.GetProtectionFactor(); // 0.0 to 1.0
    float stability = 150.0f;

    float pSpill = ((vImpact - 3.0f) * totalMass * (1.0f - protection)) / stability;
    pSpill = Math.Clamp(pSpill, 0.0f, 1.0f);

    float randomRoll = Random.Shared.NextSingle();
    if (randomRoll < pSpill)
    {
        // En az 1, en fazla sepet hacmine göre eşya dök
        int itemsToSpill = (int)Math.Ceiling(pSpill * ActiveItems.Count);
        for (int i = 0; i < itemsToSpill; i++)
        {
            if (ActiveItems.Count == 0) break;
            
            LootItem itemToEject = ActiveItems[Random.Shared.Next(ActiveItems.Count)];
            RemoveItem(itemToEject);

            // Fiziksel prop olarak dünyaya spawn et
            var prop = SpawnPhysicalLootProp(itemToEject);
            
            // Fırlama vektörünü uygula
            float randomAngle = Random.Shared.Next(-30, 30);
            Vector3 upForce = Vector3.Up * 5.0f;
            Vector3 bounceDirection = -relativeVelocity * 0.4f;
            Vector3 ejectForce = (Quaternion.FromEuler(0, randomAngle, 0) * (bounceDirection + upForce)) / itemToEject.WeightKg;
            
            prop.Rigidbody.Velocity = ejectForce;
            
            // Kırılgan ürün hasarı
            if (itemToEject.IsFragile)
            {
                prop.SetDamageState(true); // Değerini $0 yapar
                PlaySound("sfx_glass_shatter", prop.Position);
            }
        }
        PlaySound("sfx_spillage_impact", Position);
    }
}
```

---

## 6. Ağırlık ve Sürüş Fiziği Entegrasyonu (Mass-Physics Coupling)

Sepette biriken kütlenin alışveriş arabası fiziğine olan dinamik etkileri:

1. **Hızlanma Eğrisi (Acceleration Scaling):**
   \[a_{current} = a_{base} \cdot \left(1.0 - \frac{M_{total}}{M_{max\_capacity} \cdot 1.5}\right)\]
   *Sepet ağzına kadar dolduğunda ivmelenme yaklaşık %40 oranında azalır.*

2. **Frenleme Mesafesi (Braking Distance Factor):**
   \[d_{braking} = d_{base} \cdot \left(1.0 + \frac{M_{total}}{20.0}\right)\]
   *Sepetteki her 20 kg yük frenleme süresini ve kayma mesafesini iki katına çıkarır.*

3. **Gidon Titremesi ve Sapması (Wobble Threshold):**
   Sepet kütlesi arttıkça ve hız \(v_{current} > 6.0\text{ m/s}\) limitini aştığında, gidonda fiziksel sapma (titreme) genliği artar:
   \[\theta_{wobble} = \sin(\text{Time} \cdot \omega_{wobble}) \cdot \left( \frac{v_{current} \cdot M_{total}}{K_{wobble\_damp}} \right)\]

---

## 7. Gidon Gösterge Paneli ve Görev Kağıdı Tasarımı (Diegetic Display)

```text
=====================================================
|                BLACK FRIDAY - COOP                |
=====================================================
|  [ SEPET DEĞERİ ]            [ KOTA BİLGİSİ ]     |
|   $1,420 / $2,500             Kalan: -$1,080      |
|                                                   |
|  [ SEPET DOLULUK ]           [ GÜN / SAAT ]       |
|   [■■■■■■□□] 6 / 8 Slot       Akşam (Son 01:12)    |
=====================================================
|  GÖREV LİSTESİ:                                    |
|  [X] 2x Marka Parfüm ($120)                       |
|  [ ] 1x Büyük Ekran TV ($850)                      |
|  [ ] 3x Enerji İçeceği Koli ($90)                  |
=====================================================
```
*(Araba üzerindeki fiziksel ekran ve sepet kenarına mandallanmış kağıt, oyuncunun kamerasını araba yönüne çevirdiğinde diegetic olarak tamamen okunabilir durumdadır).*

