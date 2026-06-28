# Oyun Ekonomisi, Kotalar ve Sadakat Sistemi Tasarım Belgesi (Ultimate EconomySystem)

Bu belge; sabah fazında tetiklenecek absürt AVM günlük olaylarını, olay mutatör katsayılarını, C# olay veri yapılarını ve ilgili ağ veri paket (payload) yapılarını içerir.

---

## 1. Absürt AVM Günlük Olayları Kütüphanesi (Random Events Registry)

Sabah fazında panoda rastgele yayınlanacak olan olayların listesi ve bu olayların oyun dünyasındaki fizik ve ekonomi parametrelerine uygulayacağı net mutatör çarpanları:

### 1.1 Deterjan Sızıntısı (Detergent Spill)
- **Açıklama:** AVM temizlik işçileri 1. katta dev deterjan tankını devirdi!
- **Mekanik Çarpanlar:**
  - `Kat A` zemin sürtünme katsayısı: \(\mu_{friction} = 0.05\) (Normalde 0.6; zemin buz pistine döner).
  - Arabaların yanal drift açısı artış hızı: \(x2.5\).
  - Çarpışmalarda sepetten mal dökülme olasılığı: \(P_{spill} \cdot 1.5\).

### 1.2 Kara Cuma Çılgınlığı (Black Friday Frenzy)
- **Açıklama:** Yılın en büyük indirim günü geldi, AVM reyonları tıklım tıklım!
- **Mekanik Çarpanlar:**
  - Elektronik reyonu ürün fiyat çarpanı: \(x0.50\) (%50 İndirim).
  - Haritadaki Güvenlik Robotu sayısı: \(+1\) (Ek robot spawn edilir).
  - Robot devriye hızı (`PatrolSpeed`) ve kovalama hızı (`ChaseSpeed`): \(x1.20\).

### 1.3 Kasiyer Grevi (Cashier Strike)
- **Açıklama:** Kasiyerler çalışma şartlarını protesto etmek için greve yakın bir yavaşlatma eylemi yapıyor.
- **Mekanik Çarpanlar:**
  - Kasiyer sabır barı dolma katsayısı: \(C_{failed\_QTE} \cdot 1.5\) (Hata yapıldığında kasiyer çok daha hızlı öfkelenir).
  - Kasiyer rüşvet ($100) bedeli: \(x2.0\) (Öfke barını düşürmek artık $200).

### 1.4 Lüks Tüketim Vergisi (Luxury Tax)
- **Açıklama:** AVM yönetimi 3. kattaki mücevherler için ek lüks vergisi getirdi.
- **Mekanik Çarpanlar:**
  - Lüks Eşya reyonu ürün fiyat çarpanı: \(x1.80\) (%80 Fiyat artışı).
  - 3. kattaki Lazer Güvenlik Bariyeri sayısı: \(x2.0\) (İki kat fazla tuzak).
  - Güvenlik Robotu görüş menzili (`VisionRange`): \(x1.25\) (Daha uzak mesafeden algılama).

### 1.5 Güvenlik Güncellemesi (Security Firmware Update)
- **Açıklama:** Güvenlik robotlarının yazılımları en son yapay zeka sürümüyle güncellendi!
- **Mekanik Çarpanlar:**
  - Robot görüş açısı (FOV): \(90^\circ\) yerine \(110^\circ\).
  - Robot ses hassasiyeti (`HearingSensitivity`): \(x1.35\) (Çok daha sessiz şıngırtıları algılar).

---

## 2. C# Olay Yapı Şablonu (DailyEventData ScriptableObject)

Geliştiricilerin kullanacağı günlük olay veri şablonu:

```csharp
[CreateAssetMenu(fileName = "NewDailyEvent", menuName = "BlackFriday/DailyEvent")]
public class DailyEventData : ScriptableObject
{
    public string EventId;
    public string EventTitle;
    public string EventDescription;
    
    // Parametre Mutatörleri (Modifier multipliers)
    public float GlobalPriceModifier = 1.0f;
    public float ElectronicsPriceModifier = 1.0f;
    public float LuxuryPriceModifier = 1.0f;
    public float FrictionModifier = 1.0f;
    public float GuardSpeedModifier = 1.0f;
    public float GuardHearingModifier = 1.0f;
    public float BribeCostModifier = 1.0f;
    public int AdditionalGuardCount = 0;
}
```

---

## 3. Ağ Günlük Olay Değişim Paket Yapısı (DailyEventPayload)

Gün başında sunucu tarafından seçilen rastgele olayın tüm istemcilere (clients) senkronize edilmesi için kullanılan ağ veri paketi yapısı (`DailyEventPayload`):

```csharp
[Serializable]
public struct DailyEventPayload
{
    public string ActiveEventId; // Seçilen günlük olayın ScriptableObject ID'si
    public float ActiveFrictionMultiplier; // O gün geçerli olacak zemin sürtünme çarpanı
    public float ActiveGuardSpeedMultiplier; // O gün geçerli olacak robot hız çarpanı
    public float ActiveBribeCost; // O gün geçerli olacak kasa rüşvet miktarı ($)
}
```
*(Bu paket, gün başında sunucu tarafından `[Rpc.Broadcast]` ile istemcilere dağıtılır ve istemci tarafındaki HUD tabelasında haber anonsu olarak gösterilir).*

---

## 4. Enflasyon Hızı ve Dinamik Fiyatlandırma Formülleri (Economic Scaling)

Günler ilerledikçe artan ürün fiyatları ve zorluk katsayıları arasındaki matematiksel ilişki:

### 4.1 Günlük Enflasyon Formülü
\[Price(d, diff) = BaseValue \cdot \left(1.0 + \alpha_{diff} \cdot (d - 1)\right) \cdot M_{event}\]

Burada:
- \(d\): Mevcut gün/raund sırası (\(1, 2, 3, \dots\)).
- \(\alpha_{diff}\): Zorluk seviyesine göre enflasyon katsayısı:
  - Kolay Zorluk: \(\alpha_{easy} = 0.05\) (%5 günlük artış)
  - Orta Zorluk: \(\alpha_{medium} = 0.10\) (%10 günlük artış)
  - Zor Zorluk: \(\alpha_{hard} = 0.18\) (%18 günlük artış)
- \(M_{event}\): Aktif günlük olayın fiyat çarpanı (örn. Lüks Tüketim Vergisi için \(1.8\)).

### 4.2 Vergi Kaçırma ve Yakalanma Cezası (Tax Evasion Penalties)
Kasaya uğramadan veya ödeme yapmadan doğrudan kaçış kapısına yönelen oyuncunun yakalanması durumunda mallarına el konur ve ek borç cezası yazılır:
\[Penalty_{tax} = Value_{stolen} \cdot (1.0 + \beta_{penalty})\]
Burada:
- \(\beta_{penalty}\): Kaçırma cezası çarpanı (\(0.50\)). Çalınan malların toplam değerine ek olarak yarısı kadar borç hanesine ekleme yapılır.

---

## 5. AVM Sadakat Puanı ve Kalıcı İlerleme Veri Şeması (Loyalty Save Layout)

Oyuncuların her run sonunda kazandığı kalıcı AVM Sadakat Puanı (Cashback) ve lobiden satın alınan kozmetik eşyaların Steam Cloud/Yerel dosya kayıt sistemi şeması:

```json
{
  "SteamId": 76561198000000000,
  "TotalLoyaltyPoints": 345,
  "LifetimeEarningsUSD": 84200,
  "UnlockedCosmetics": {
    "Skins": [
      "skin_zengin_emekli",
      "skin_kasli_fit"
    ],
    "NeonLights": [
      "neon_red_glow",
      "neon_cyberpunk_cyan"
    ],
    "Horns": [
      "horn_classic_truck",
      "horn_clown_honk"
    ]
  },
  "CurrentEquipped": {
    "ActiveSkin": "skin_zengin_emekli",
    "ActiveNeon": "neon_cyberpunk_cyan",
    "ActiveHorn": "horn_clown_honk"
  }
}
```

```csharp
public class SaveSystem
{
    private const string SaveFolder = "saves";

    public static void SaveProfile(ulong steamId, SaveData data)
    {
        string json = Json.Serialize(data);
        FileSystem.AppData.WriteAllText($"{SaveFolder}/{steamId}.json", json);
    }

    public static SaveData LoadProfile(ulong steamId)
    {
        string path = $"{SaveFolder}/{steamId}.json";
        if (!FileSystem.AppData.FileExists(path))
        {
            return SaveData.CreateDefault(steamId);
        }
        
        string json = FileSystem.AppData.ReadAllText(path);
        return Json.Deserialize<SaveData>(json);
    }
}
```

