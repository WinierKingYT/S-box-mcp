# Alışveriş Arabası ve Sürüş Fiziği Tasarım Belgesi (Ultimate ShoppingCartSystem)

Bu belge; C# API arayüz imzalarını, Kara Borsa parça yükseltme yollarını (Tier 1/2/3) ve 3D uzamsal ses azaltma (attenuation) parametrelerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin araba ve sürüş mekaniklerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
/// <summary>
/// Sürülebilir tüm fiziksel araçların uygulaması gereken temel arayüz.
/// </summary>
public interface IDriveable
{
    /// <summary>
    /// Araca motor gücü ve yönlendirme girdilerini iletir.
    /// </summary>
    void ApplyDriveInputs(Vector2 movementInput, bool isDrifting, bool isNitroActive);

    /// <summary>
    /// Aracın anlık Rigidbody hız vektörünü döner.
    /// </summary>
    Vector3 GetVelocity();

    /// <summary>
    /// Sürücünün aracı bıraktığında momentumun aktarılmasını sağlar.
    /// </summary>
    void ReleaseControl(Vector3 launchForce);
}

/// <summary>
/// Alışveriş arabası üzerindeki modüler parça slotlarını yöneten arayüz.
/// </summary>
public interface IVehicleUpgradeSystem
{
    /// <summary>
    /// Belirtilen slota yeni bir modifikasyon parçası takar.
    /// </summary>
    bool EquipPart(PartSlot slot, CartUpgradeItem part);

    /// <summary>
    /// Belirtilen slotta takılı olan parçayı söker ve geri döner.
    /// </summary>
    CartUpgradeItem UnequipPart(PartSlot slot);

    /// <summary>
    /// Aktif yükseltmelerin kullanım haklarını (şarjlarını) sorgular.
    /// </summary>
    int GetRemainingCharges(PartSlot slot);
}
```

---

## 2. Kara Borsa Parçaları Seviye Yükseltme Yolu (Upgrade Paths)

Her araba parçasının lobide kalıcı AVM Sadakat Puanı (Cashback) ile yükseltilebilen 3 aşamalı (Tier) parametre değerleri:

### 2.1 V8 Motor (Nitro - Arka Slot)
- **Tier 1 (Standart):** Maliyet: 30 Puan. Süre: \(2.0\text{ sn}\). Cooldown: \(15\text{ sn}\). Tork Çarpanı: \(x2.0\).
- **Tier 2 (Gelişmiş):** Maliyet: 60 Puan. Süre: \(3.0\text{ sn}\). Cooldown: \(12\text{ sn}\). Tork Çarpanı: \(x2.5\).
- **Tier 3 (Modifiye):** Maliyet: 100 Puan. Süre: \(4.5\text{ sn}\). Cooldown: \(8\text{ sn}\). Tork Çarpanı: \(x3.0\).

### 2.2 Ön Tampon (Bulldozer - Ön Slot)
- **Tier 1 (Çelik Çerçeve):** Maliyet: 25 Puan. Dökülme Koruması: %50. Geri İtme Kuvveti: \(x1.2\).
- **Tier 2 (Güçlendirilmiş):** Maliyet: 50 Puan. Dökülme Koruması: %80. Geri İtme Kuvveti: \(x1.6\).
- **Tier 3 (Ağır Zırh):** Maliyet: 85 Puan. Dökülme Koruması: %100 (Sıfır Dökülme). Geri İtme Kuvveti: \(x2.0\).

### 2.3 Manyetik Sepet (Sepet Slotu)
- **Tier 1 (Zayıf Mıknatıs):** Maliyet: 20 Puan. Çekim Menzili: \(1.0\text{ metre}\). Algılama Sıklığı: saniyede 1 kez.
- **Tier 2 (Neodimyum Mıknatıs):** Maliyet: 45 Puan. Çekim Menzili: \(1.5\text{ metre}\). Algılama Sıklığı: saniyede 2 kez.
- **Tier 3 (Elektromıknatıs):** Maliyet: 75 Puan. Çekim Menzili: \(2.0\text{ metre}\). Algılama Sıklığı: saniyede 5 kez (anlık çekim).

---

## 3. Ses Varlık Şartnamesi (Audio Attenuation & Cues)

Arabanın 3D uzaysal seslerinin s&box ses motorundaki (Sound Event) sönümlenme ve azaltma parametreleri:

| Ses Olayı (Sound Event) | Min Mesafe (Tam Ses) | Max Mesafe (Sessiz) | Azaltma Eğrisi (Roll-off) | Döngü (Loop) |
|-------------------------|----------------------|---------------------|---------------------------|--------------|
| `sfx_cart_roll` (Tekerlek Rulo) | \(1.5\text{ metre}\) | \(15.0\text{ metre}\)| Logarithmic | Evet (Hıza bağlı pitch) |
| `sfx_wobble_shaking` (Titreme) | \(0.5\text{ metre}\) | \(8.0\text{ metre}\) | Linear | Evet (Hıza bağlı volume) |
| `sfx_tire_screech` (Drift) | \(2.0\text{ metre}\) | \(20.0\text{ metre}\)| Logarithmic | Evet |
| `sfx_nitro_burst` (Nitro Patlama) | \(5.0\text{ metre}\) | \(35.0\text{ metre}\)| Logarithmic | Hayır |

---

## 4. Alışveriş Arabası Sürüş Fiziği Entegrasyonu (Physics & Controls Simulation)

s&box Motorunun `Simulate` döngüsü içerisinde kullanılacak fiziksel tork, sürtünme ve yönlendirme kuvvetlerinin hesaplanması:

### 4.1 İleri/Geri Motor Kuvveti Hesaplaması
Aracın anlık hızlanma ivmesi ve torku şu formülle hesaplanır:
\[\vec{F}_{drive} = \vec{d}_{forward} \cdot T_{motor} \cdot y_{input} \cdot \eta_{nitro}\]
Burada:
- \(\vec{d}_{forward}\): Arabanın anlık ileri doğrultu vektörü.
- \(T_{motor}\): Motor temel tork değeri (\(250.0\text{ N}\)).
- \(y_{input}\): Sürücü dikey girdi değeri (\([-1.0, 1.0]\)).
- \(\eta_{nitro}\): Nitro aktif ise (\(x2.0\) ila \(x3.0\) arası) motor tork çarpanı.

### 4.2 Yanal Kayma ve Drift (Lateral Friction & Drift Slerp)
Drift yapılmadığı durumlarda araba tekerleklerinin yolu tutması için yanal hız sönümlendirilir. Drift tuşuna basıldığında ise yanal sürtünme azaltılarak kontrollü kayma sağlanır:
\[\vec{v}_{lateral} = \vec{d}_{right} \cdot (\vec{v}_{current} \cdot \vec{d}_{right})\]
\[\vec{v}_{corrected} = \vec{v}_{current} - \vec{v}_{lateral} \cdot (1.0 - k_{drift\_slip})\]
Burada:
- \(k_{drift\_slip}\): Drift aktifken \(0.85\) (büyük oranda kayma), pasifken \(0.1\) (yolu sıkı tutma).

---

## 5. Kritik Sürüş Durumları ve Sıra Dışı Senaryolar (Edge Cases)

### 5.1 Hijack (Arabayı Çalma/Kaçırma) Mekaniği
Bir oyuncu başka bir oyuncunun sürdüğü arabayı arkadan yakalayıp çalmaya çalıştığında (E tuşu etkileşimi):
- **Koşul:** Saldırganın arabaya olan uzaklığı \(\le 1.8\text{ metre}\) olmalı ve arabanın arkasında bulunmalıdır.
- **Mekanik:** Saldırgan E tuşuna bastığında bir fiziksel itme uygulayarak mevcut sürücüyü arabadan fırlatır:
  \[\vec{F}_{eject} = (\vec{d}_{right} \cdot \text{Random}(-1.0, 1.0) + \vec{u}_{up} \cdot 0.5) \cdot 120.0\text{ N}\]
- **Ceza:** Sürücü arabadan düşerek \(1.2\text{ saniye}\) boyunca sersemler (stun). Saldırgan ise otomatik olarak arabayı sürme pozisyonuna geçer.

### 5.2 Yüksek Hızlı Çarpışmalar (High-Speed Collisions)
Araba \(v_{current} \ge 8.0\text{ m/s}\) hızla bir duvara veya reyon rafına çarptığında:
- **Raf Yıkımı:** Raf tamamen parçalanır ve NavMesh dinamik olarak güncellenir.
- **Sürücü Fırlaması:** Sürücü alışveriş arabasının üzerinden ileriye doğru ragdoll olarak fırlatılır:
  \[\vec{v}_{ragdoll} = \vec{v}_{collision\_velocity} \cdot 1.5\]
- **Sepet Boşalması:** Sepetteki kırılgan olmayan eşyaların %80'i, kırılgan olanların ise %100'ü (kırılarak $0 değerine düşecek şekilde) çevreye saçılır.

---

## 6. Görsel Efekt Şartnamesi (VFX Particle Events)

Arabanın fiziksel durumlarına bağlı olarak tetiklenecek s&box partikül efektleri:

| VFX Tetikleyici Durumu (VFX Event) | Partikül Adı (Particle System) | Spawn Noktası (Attachment Point) | Açıklama |
|-------------------------------------|--------------------------------|----------------------------------|----------|
| Nitro Aktifken                      | `particles/nitro_exhaust.vpcf` | Tekerlekler ve Motor Çıkışı     | Parlak mavi alev ve duman bulutu |
| Lastik Kayması / Drift              | `particles/tire_skid_smoke.vpcf`| Arka İki Tekerlek Temas Noktası  | Siyah aşınma izleri ve gri toz |
| Yüksek Hızlı Duvar Çarpması         | `particles/cart_metal_sparks.vpcf`| Ön Tampon Çarpma Noktası       | Kıvılcım saçılması ve toz bulutu |
| Yağ Tankı Sızıntısı (Aktif Parça)   | `particles/oil_leak_spill.vpcf`| Arka Dingil Alt Noktası          | Yere yapışan ve kayganlık veren siyah sıvı |

---

## 7. s&box Giriş Eşlemeleri ve Sürüş Kontrolleri (Input Actions Bindings)

Alışveriş arabasının sürüş ve aksiyon mekanikleri için s&box girdi motoruna (`Input.Down`, `Input.Pressed` vb.) tanımlanmış tuş şemaları:

| Girdi Eylemi (Input Action) | Varsayılan Tuş (Default Key) | Tetiklenen Durum (Simulation State) | Açıklama / Davranış |
|-----------------------------|------------------------------|-------------------------------------|---------------------|
| `use`                       | `[E]`                        | Araca Binme / İtme Başlatma        | Boş arabaya yaklaşınca binmeyi, başka oyuncu sürerken arkasından basılırsa **Hijack** tetikler. |
| `attack1` (Basılı Tutma)    | `[LMB] (Sol Tık)`            | Arabayı Sürme (Hold to Drive)       | Basılı tutulduğu sürece ileri tork uygulanır. Direksiyon yönü kameranın baktığı yöne doğru yaw açısıyla dönelir. |
| `attack1` (Bırakma)         | `[LMB] Release`              | Fırlatma (Launch / Release)        | LMB bırakıldığında araba oyuncunun elinden kurtulur ve birikmiş ivmeyle ileri doğru fırlatılır. |
| `attack2`                   | `[RMB] (Sağ Tık)`            | Drift Modu (Slide)                  | Yanal sürtünmeyi %85 azaltır, araba keskin virajlarda kaymaya başlar. |
| `run`                       | `[Shift]`                    | Nitro Aktivasyonu                   | V8 Motor parçası takılıysa ve şarjı varsa ileri yönlü tork çarpanını tetikler. |
| `duck`                      | `[Ctrl]`                     | Yağ Tankı Bırakma                   | Egzoz Yağ Tankı takılıysa ve şarjı varsa arkaya kaygan yağ birikintisi bırakır. |
| `slot1` - `slot3`           | `[1]` - `[3]`                | Cep Hızlı Seçim                     | Cep envanterindeki 3 slottan birindeki ürünü eline almak için seçer. |

