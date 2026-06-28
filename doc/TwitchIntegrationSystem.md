# Twitch Canlı Yayıncı Entegrasyonu ve İzleyici Etkileşim Sistem Tasarım Belgesi (Ultimate TwitchIntegrationSystem)

Bu belge; Twitch IRC bağlantısını, oylama şemalarını, izleyici sohbet komutlarını ve ekranda belirecek oylama arayüzlerini (HUD) tanımlayan nihai teknik şartnameyi içerir.

---

## 1. C# API Arayüzleri ve Metot İmzaları (C# Interfaces)

Geliştiricilerin Twitch entegrasyonu ve oylama yöneticilerini kodlarken uygulayacağı temel C# arayüz şemaları:

```csharp
public struct TwitchVoteOption
{
    public int Index;
    public string Description;
    public int CurrentVotes;
}

/// <summary>
/// Twitch IRC sohbetine bağlanarak izleyici oylarını dinleyen arayüz.
/// </summary>
public interface ITwitchIntegrationService
{
    /// <summary>
    /// Belirtilen yayıncı kullanıcı adı ile Twitch IRC sohbetine bağlanır.
    /// </summary>
    void ConnectToChannel(string channelName);

    /// <summary>
    /// Sohbet bağlantısını keser.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Ekranda yeni bir izleyici oylaması başlatır.
    /// </summary>
    void StartVote(string question, List<string> options, float durationSeconds);

    /// <summary>
    /// Oylamayı zorla veya süre sonunda kapatarak kazanan seçeneği uygular.
    /// </summary>
    void ResolveActiveVote();
}
```

---

## 2. İzleyici Oylama Seçenekleri ve Mutatör Etkileri (Vote Events Registry)

Oyun esnasında her 3 dakikada bir tetiklenen ve oyuna doğrudan etki eden absürt izleyici oylama havuzu:

### 2.1 Oylama Seçenekleri ve Etkileri
- **Seçenek 1 (!oy 1 - Elektrik Kesintisi):**
  - **Açıklama:** AVM ışıklarını 15 saniyeliğine kapatır (Gece fazı gibi).
  - **Mutatör:** Robotların görüş menzili \(VisionRange = 2.0\text{ m}\) olur.
- **Seçenek 2 (!oy 2 - Robot Öfkesi):**
  - **Açıklama:** Güvenlik robotları 30 saniyeliğine hızlanır.
  - **Mutatör:** Robot kovalama hızı (\(ChaseSpeed\)) \(x1.35\) olur.
- **Seçenek 3 (!oy 3 - Kara Cuma Kuponu):**
  - **Açıklama:** Oyuncuların ceplerine anında $300 değerinde alışveriş kuponu yerleştirir.
  - **Mutatör:** İlgili envantere kupon eşyası spawn edilir.
- **Seçenek 4 (!oy 4 - Kasiyer Çıldırdı):**
  - **Açıklama:** Kasiyerlerin sabır barı anında %50 azalır.

---

## 3. Twitch Oylama HUD Arayüzü (Twitch Voting ASCII Mockup)

Yayıncı oyun içindeyken ekranın sağ üst köşesinde belirecek olan ve sohbetten gelen oyların anlık dağılımını gösteren HUD tasarımı:

```text
=====================================================
|               TWITCH YAYINCI OYLAMASI             |
=====================================================
|  Soru: İzleyiciler bir sonraki olayı seçiyor!      |
|  Kalan Süre: 14 saniye                            |
=====================================================
|  [1] Elektrik Kesintisi (Sürenin %80'i loş ışık)   |
|      Oy Oranı: %45 [■■■■■■■■■□□□□□] (450 Oy)       |
|                                                   |
|  [2] Robot Öfkesi (Güvenlik hızı +%35)             |
|      Oy Oranı: %30 [■■■■■■□□□□□□□□] (300 Oy)       |
|                                                   |
|  [3] Kara Cuma Kuponu (Herkes cepten +$300)       |
|      Oy Oranı: %25 [■■■■■□□□□□□□□□] (250 Oy)       |
=====================================================
|  * Sohbetten katılım için: !oy 1, !oy 2, !oy 3    |
=====================================================
```
*(Oylama sona erdiğinde, kazanan seçenek ekranın ortasında flaş efekt ile duyurulur ve ilgili C# mutatörü global state'e anında uygulanarak ağ üzerinde senkronize edilir).*

---

## 4. Sohbet Ayrıştırma ve Spam Filtresi Kuralları (Message Parsing & Filters)

Yayıncı sohbetinin yoğunluğuna göre oylamanın doğruluğunu ve performansını koruma kuralları:

1. **Tekil Oy Doğrulaması (One Vote Per User):**
   Aynı Twitch kullanıcısından gelen mükerrer oylar elenir. Sistem aktif oylama süresince oy kullanan kullanıcıların adlarını benzersiz bir kümede (`HashSet<string>`) tutar.
2. **Düzenli İfade (Regex) Eşleşme Ayrıştırıcısı:**
   Gelen sohbet mesajları şu regex kalıbına göre test edilir ve sadece geçerli oylar sayılarak işlenir:
   ```csharp
   private static readonly Regex VotePattern = new Regex(@"^!oy\s+([1-4])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

   public void OnChatMessageReceived(string username, string message)
   {
       if (hasVotedUsers.Contains(username)) return; // Çift oy engeli
       
       var match = VotePattern.Match(message.Trim());
       if (match.Success)
       {
           int optionIndex = int.Parse(match.Groups[1].Value);
           RegisterVote(optionIndex);
           hasVotedUsers.Add(username);
       }
   }
   ```

---

## 5. Twitch Ağ Kesintileri ve Yedek Planlar (Twitch Disconnection Fallbacks)

IRC sunucusuyla olan bağlantının kopması durumunda oyun akışının bozulmaması için uygulanan yedek plan protokolü:

1. **Bağlantı Kaybı Tespiti:**
   Eğer TCP soketi 15 saniye boyunca sunucudan heartbeat (PING/PONG) sinyali alamazsa veya soket kapanırsa, `OnTwitchConnectionLost` tetiklenir.
2. **Yedek Mekanik (Automated Random Events):**
   Bağlantı koptuğunda, ekranın sağ üst köşesindeki Twitch HUD paneli yumuşakça gizlenir. Sistem, Twitch oylamaları yerine otomatik olarak her 3 dakikada bir `EconomySystem.md` içerisindeki **AVM Günlük Olaylar** kütüphanesinden rastgele bir mutatör seçip oyuna enjekte eder. Oyuncu kesintisiz oynamaya devam eder.

---

## 6. Yayıncı Sadakat Çarpanları ve Yetki Bazlı Oy Ağırlıkları (Vote Weighting)

Twitch sohbetindeki farklı izleyici gruplarının oylarının ağırlıklandırılması:

- **Abone Çarpanı (Subscriber Multiplier):** Abone (Subscriber) statüsündeki izleyicilerin oyları çift sayılır (\(x2\) ağırlık).
- **VIP Çarpanı (VIP Multiplier):** VIP rozetine sahip kullanıcıların oyları üç katı değerindedir (\(x3\) ağırlık).
- **Moderatör ve Yayıncı Çarpanı:** Moderatör veya yayıncının kendisinin kullandığı oylar beş katı sayılır (\(x5\) ağırlık).

### 6.1 Ağırlık Hesaplama Logic'i
```csharp
public int GetUserVoteWeight(TwitchUserBadgeStatus badges)
{
    if (badges.IsBroadcaster || badges.IsModerator) return 5;
    if (badges.IsVip) return 3;
    if (badges.IsSubscriber) return 2;
    return 1;
}

public void ProcessIncomingVote(string username, int optionIndex, TwitchUserBadgeStatus badges)
{
    if (hasVotedUsers.Contains(username)) return;

    int weight = GetUserVoteWeight(badges);
    voteCounts[optionIndex] += weight;
    hasVotedUsers.Add(username);
}
```

---

## 7. Chat Spam ve Bot Engelleme Koruması (Anti-Spam Rate Limiter)

Sohbet akışının oylama sunucusunu tıkamasını engellemek için kullanılan filtre sınırlamaları:

- **Mesaj Sıklığı Limiti (Rate Limiting):** Tek bir Twitch kullanıcısı saniyede en fazla \(1\text{ adet}\) mesaj gönderebilir. Bu sıklığı aşan kullanıcıların mesajları oylama veritabanına alınmadan elenir.
- **Bot Hesabı Süzgeci:** Oylama süresince sadece `!oy 1` gibi saf komutlar ayrıştırılır, komut içermeyen genel spam sohbet mesajları thread'i bloke etmeden asenkron olarak yoksayılır.


