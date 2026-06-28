# Başarımlar, İstatistikler ve Steamworks Entegrasyon Tasarım Belgesi (Ultimate AchievementStatsSystem)

Bu belge; oyuncu veri analizlerini, Steamworks istatistik eşleşmelerini, başarım kilit açma koşullarını ve istatistik şemalarını tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin istatistik ve başarım takip sistemlerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
/// <summary>
/// Oyuncu istatistiklerini ve Steam başarımlarını yöneten arayüz.
/// </summary>
public interface IStatsTracker
{
    /// <summary>
    /// Belirtilen sayısal istatistiği belirli bir miktar artırır veya günceller.
    /// </summary>
    void IncrementStat(string statName, float amount);

    /// <summary>
    /// Belirtilen istatistiğin anlık değerini döner.
    /// </summary>
    float GetStatValue(string statName);

    /// <summary>
    /// Koşulları sağlanan başarımların kilitlerini Steamworks API üzerinden açar.
    /// </summary>
    void CheckAndUnlockAchievements();

    /// <summary>
    /// Tüm istatistikleri ve kazanımları yerel ve bulut dosya sistemine kaydeder.
    /// </summary>
    void SaveStatsAndAchievements();
}
```

---

## 2. İstatistikler Kütüphanesi ve Steamworks Eşleme Şeması (Stats Registry)

Oyuncu profilinde saklanan ve Steamworks API panelinde takip edilen temel istatistik veri değişkenleri:

- `stat_total_loot_value_usd` (Toplam soyulan mal değeri): Toplam USD değeri biriktikçe Steamworks tarafına güncellenir.
- `stat_total_trolley_crashes` (Reyon devirme sıklığı): Oyuncunun arabayla hızı \(\ge 8\text{ m/s}\) iken çarparak yıktığı reyon sayısı.
- `stat_total_teammates_rescued` (Takım arkadaşı kurtarma): Nezarethaneden lockpick ile kurtarılan arkadaş sayısı.
- `stat_total_guard_stuns` (Robotları sersemletme): Yangın söndürücü veya ağır eşya fırlatarak kısa devre yapılan robot sayısı.
- `stat_total_times_jailed` (Nezarete düşme sayısı): Güvenlik robotları tarafından yakalanma sıklığı.

---

## 3. Başarım Kilit Açma Koşulları ve Kriterleri (Achievements Criteria)

Oyuncuların oyun içi aksiyonlarına bağlı olarak kilitleri açılan s&box Steam başarımları ve bunların doğrulanma formülleri:

### 3.1 Hızlı ve Öfkeli (Fast & Furious Shop)
- **Koşul:** Tek bir raund içinde arabayla drift yaparak toplamda en az \(100\text{ metre}\) yol almak ve reyon devirmek.
- **Kriter:** `stat_total_drift_meters >= 100` VE `stat_trolley_crashes >= 1`.

### 3.2 Büyük Vurgun (Grand Theft AVM)
- **Koşul:** Tek bir maçı en az \(4,000\text{ USD}\) değerinde malı başarıyla kaçırarak tamamlamak.
- **Kriter:** `session_escape_value >= 4000`.

### 3.3 Özgürlük Savaşçısı (Prison Break)
- **Koşul:** Aynı maç içerisinde hem havalandırmadan kendisi kaçmış, hem de en az bir kez arkadaşını lockpick ile kurtarmış olmak.
- **Kriter:** `session_vent_escapes >= 1` VE `session_rescued_friends >= 1`.

### 3.4 Kasiyerin Kabusu (Cashier's Nightmare)
- **Koşul:** Bir kasiyere hatalı QTE basarak veya arabayla çarparak Kasiyer Öfkesini (\(A_{anger}\)) 3 kez \(100\) limitine ulaştırıp fırlatılmak.
- **Kriter:** `stat_total_cashier_rages_triggered >= 3`.

---

## 4. Başarım Kilit Açma Arayüzü (Achievement Toast Layout)

Bir başarım açıldığında ekranın sol alt köşesinde belirecek olan diegetic s&box UI bildirim (Toast) tasarımı:

```text
=====================================================
|  [🏆] BAŞARIM KAZANILDI!                          |
=====================================================
|  Adı: Kasiyerin Kabusu                             |
|  Açıklama: Kasiyeri 3 kez çıldırtıp fırlatıl.     |
|                                                   |
|  [ +150 AVM Sadakat Puanı Ödülü Kazanıldı! ]      |
=====================================================
```

---

## 5. Bulut Senkronizasyon Çatışmaları ve Güvenlik İstisnaları (Data Integrity)

Kullanıcı verilerinin manipülasyonunu engellemek ve bulut veri uyuşmazlıklarını çözme kuralları:

1. **Bulut ve Yerel Dosya Çatışması (Cloud vs Local Conflict):**
   - *Senaryo:* İstemci çevrimdışı oynayıp Cashback kazandıktan sonra çevrimiçi olduğunda buluttaki veriyle yereldeki veri uyuşmazsa.
   - *Çözüm:* Sunucu-tabanlı zaman damgası (`LastSavedTimestamp`) doğrulaması yapılır. Hangi veri paketinin zaman damgası daha güncelse o veri paketi geçerli sayılır.
2. **Hile ve İstatistik Manipülasyonu Koruması (Anti-Cheat / Validation):**
   - Yerel `saves/*.json` dosyasını elle düzenleyerek Cashback puanını haksız artırmaya çalışan istemcilere karşı, profildeki tüm veriler gizli bir sunucu anahtarı (Salt) kullanılarak SHA-256 algoritmasıyla hash'lenir ve profile bir imza değeri (`SecuritySignature`) eklenir:
     \[Signature = \text{SHA256}(SteamId + CashbackPoints + \text{Salt})\]
   - Dosya yüklenirken hash tekrar hesaplanır; eğer profile yazılmış imza değeri ile uyuşmuyorsa, profil geçersiz sayılır ve hileli işlem olarak sıfırlanır.

