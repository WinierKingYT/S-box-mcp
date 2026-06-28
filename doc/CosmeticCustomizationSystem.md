# Kozmetik Özelleştirme, Garaj ve Lobi Sistemleri Tasarım Belgesi (Ultimate CosmeticCustomizationSystem)

Bu belge; lobi garajındaki modifikasyon slotlarını, neon ışıklandırma parametrelerini, korna tetikleyicilerini ve liderlik panosu (Leaderboard) veri sıralama modellerini tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin kozmetik özelleştirme ve lobi garaj sistemlerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
public enum CosmeticSlot
{
    CharacterSkin, // Oyuncu model görünümü
    CartNeonColor,  // Alışveriş arabası neon alt aydınlatması
    CartHornSound   // Alışveriş arabası korna sesi
}

/// <summary>
/// Kozmetik özelleştirme yapılabilecek nesnelerin (Oyuncu, Araba) uyması gereken arayüz.
/// </summary>
public interface ICustomizable
{
    /// <summary>
    /// Belirtilen kozmetik slota yeni bir öğe yerleştirir.
    /// </summary>
    bool ApplyCosmetic(CosmeticSlot slot, string itemId);

    /// <summary>
    /// Belirtilen slotta aktif takılı olan kozmetiğin ID'sini döner.
    /// </summary>
    string GetActiveCosmeticId(CosmeticSlot slot);

    /// <summary>
    /// Sınıf veya kozmetik değişim anında görsel modeli ağ üzerinde yeniler.
    /// </summary>
    void RebuildVisualModel();
}
```

---

## 2. Garaj Neon Aydınlatma ve Korna Ses Parametreleri (Garage & Items)

Lobide yer alan garaj dükkanında satılan kalıcı öğelerin nitelikleri ve s&box motoru teknik özellikleri:

### 2.1 Neon Alt Aydınlatma Şartnamesi (Neon Underglow)
Neonlar arabanın altına yerleştirilen dinamik bir s&box `PointLightEntity` ile simüle edilir:
- **Işık Menzili (Range):** \(1.5\text{ metre}\) yarıçap.
- **Işık Yoğunluğu (Intensity):** \(15.0\text{ lux}\).
- **Renk Paleti (Color Items):**
  - `neon_cyberpunk_cyan`: Cyan (`#00F5FF` - R:0, G:245, B:255).
  - `neon_synthwave_pink`: Pembe (`#FF007F` - R:255, G:0, B:127).
  - `neon_toxic_green`: Yeşil (`#39FF14` - R:57, G:255, B:20).

### 2.2 Korna Ses Event Tetikleyicileri (Horn Sounds)
Korna çalma aksiyonu (Lobi veya oyun içinde `R` tuşuna atalı olabilir) bir s&box `SoundEvent` çalıştırır:
- `horn_truck`: Ağır tonlu klasik kamyon kornası (Pitch: \(0.8\)).
- `horn_clown`: Absürt palyaço kornası (Pitch: \(1.2\)).
- `horn_police`: Kısa siren uyarısı (Pitch: \(1.0\)).

---

## 3. Lobi Liderlik Panosu Veri Sıralama Modeli (Leaderboard Model)

Kalıcı Cashback puanına göre oyuncuları listeleyen liderlik panosu veri paketi ve C# sıralama mantığı:

```csharp
[Serializable]
public struct LeaderboardEntry
{
    public string DisplayName;
    public ulong SteamId;
    public int CashbackPoints;
    public float TotalTimePlayedHrs;
}

public class LeaderboardManager
{
    /// <summary>
    /// Tüm oyuncu listesini Cashback puanına göre yüksekten düşüğe doğru sıralar.
    /// </summary>
    public static List<LeaderboardEntry> SortLeaderboard(List<LeaderboardEntry> unsortedEntries)
    {
        return unsortedEntries
            .OrderByDescending(entry => entry.CashbackPoints)
            .ThenByDescending(entry => entry.TotalTimePlayedHrs)
            .ToList();
    }
}
```
*(Liderlik panosu lobideki dev reklam ekranında diegetic olarak yerleştirilmiştir. Sıralama verileri güncellendikçe ekran otomatik olarak s&box UI Panel component'i ile asenkron yenilenir).*

---

## 4. Lobi Kozmetik Market Fiyat Kataloğu (Cosmetics Shop Catalog)

AVM Sadakat Puanı (Cashback) ile lobi garajındaki dükkandan satın alınabilen ürünlerin maliyet şeması:

| Kozmetik Türü (Slot) | Eşya ID (Item ID) | Açıklama / Görsel Efekt | Puan Maliyeti (Cashback) |
|----------------------|-------------------|-------------------------|--------------------------|
| Karakter Görünümü    | `skin_emekli`     | Çiçekli tatil gömleği ve terlikli ihtiyar | \(50\text{ Puan}\) |
| Karakter Görünümü    | `skin_fit`        | Dar spor atleti ve kaslı model | \(75\text{ Puan}\) |
| Karakter Görünümü    | `skin_ninja`      | Siyah kukuletalı hızlı yağmacı kıyafeti | \(120\text{ Puan}\) |
| Neon Alt Işık        | `neon_red_glow`   | Kırmızı sabit neon halkası | \(30\text{ Puan}\) |
| Neon Alt Işık        | `neon_pulse`      | Sürekli parlayıp sönen mavi/mor neon | \(60\text{ Puan}\) |
| Korna Sesi           | `horn_truck`      | Yüksek desibelli kamyon havalı kornası | \(25\text{ Puan}\) |
| Korna Sesi           | `horn_musical`    | Ritmik panayır melodisi çalan korna | \(45\text{ Puan}\) |

---

## 5. Profil Hataları ve Veri Çatışma Senaryoları (Offline Fallbacks)

Bulut dosya eşleşmelerinde veya ağ kesintilerinde veri bütünlüğünü koruma protokolü:

1. **Steamworks Bağlantı Hatası (Steam Offline):**
   - *Etki:* Oyuncu oyunu başlattığında Steam sunucularına ulaşılamazsa profil yüklenemez.
   - *Çözüm:* Sistem otomatik olarak yerel diskteki şifreli `saves/offline_backup.dat` dosyasını arar. Eğer dosya varsa Cashback puanları yerel diskten çekilir, yoksa sıfırdan geçici bir misafir profil oluşturulur. Bir sonraki başarılı Steam bağlantısında yerel profil bulutla senkronize edilir.
2. **Bozuk Profil Dosyası Algılama (Data Corruption Exception):**
   - *Etki:* Yüklenen JSON formatındaki dosya bozuk veya manuel olarak manipüle edilmişse (bütünlük kontrol hatası).
   - *Çözüm:* JSON ayrıştırma esnasında `SerializationException` tetiklenirse, dosya karantinaya alınır (`saves/corrupted_profile.bak`) ve sistem varsayılan değerlerle (`SaveData.CreateDefault()`) temiz bir profil oluşturur.

