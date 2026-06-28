---
tags: [sbox, project, gdd, design-doc]
title: "Game Design Document — Black Friday"
aliases: [bf-gdd, black-friday-gdd]
---

# Black Friday: The Game — Game Design Document

| Meta            | Detay |
|-----------------|-------|
| **Dokuman**     | Game Design Document (GDD) |
| **Versiyon**    | v0.8 — Loophole Cozumleri Eklendi |
| **Tur**         | Hiper-Aktif, Kaotik Parti, Fizik Tabanli Roguelike / Ekonomi-Finansal Hayatta Kalma |
| **Oyuncu**      | 1-4 (PvP FFA / 2v2 Takim) |
| **Platform**    | PC |
| **Motor**       | s&box (Source 2) |
| **Dagitim**     | Steam (ucretli) + s&box Workshop (ucretsiz) |
| **Hedef Kitle** | Parti oyunu severler (Jackbox, Pummel Party, Gang Beasts kitlesi) |
| **Fiyatlandirma** | s&box: ucretsiz / Steam: tek seferlik odeme |
| **Beta Planı**   | Önce kapalı beta (arkadaş/aile), sonra açık beta |
| **Kontroller**  | Klavye + Fare (tuş yeniden atama destekli) |
| **Sanat Stili** | Toy & Toon |
| **Yas Siniri**  | PEGI 12 / E10+ |
| **Dil Destegi** | Çoklu dil (TR, EN, DE, FR, ES +) |
| **İlhamlar**   | Mario Kart, Lethal Company, Monopoly, Overcooked, Metal Gear Solid |

---

## 1. Executive Summary (Yonetici Ozeti)

Black Friday: The Game, oyuncuların bir AVM'de alışveriş arabalarıyla yarıştığı, birbirini sabote ettiği, hırsızlık yaptığı ve günlük finansal kotaları karşılamak için mücadele ettiği kaotik bir parti oyunudur. Her oyun seansı (run), artan zorlukla ilerleyen bir döngüdür. Amaç hayatta kalan son kişi olmak veya belirlenen maksimum gün sayısına ulaşmaktır.

---

## 2. Core Game Loop (Temel Oyun Dongusu)

```
[AÇIK SAATLER: Yağma, Savaş & Kota Ödemesi (İlk %80 Süre)]
                         |
                         v
[KAPANIŞ VE GECE FAZI: Kepenkler İnmesi, Gece Bekçileri & Kaçış (Son %20 Süre)]
                         |
                         v
[KARA BORSA: Gece Mağazası & Yatırım (Maç Sonu Lobisi / Arayüzü)] ---> YENİ GÜN (döngü devam eder)
```

Her aşama kendine has dinamikleriyle kaotik ve eğlenceli bir deneyim sunar.

---

## 3. Oynanis ve Kurallar

### 3.1 Oyuncu Sayisi ve Modlar

| Mod | Aciklama |
|-----|----------|
| **PvP FFA** | 1-4 oyuncu, herkes kendine karsi |
| **2v2 Takim** | 4 oyuncu, iki takim, tek araba iki kisilik (Driver + Gunner) |
| **Bot**          | Lobi dolana kadar basit dummy botlar (sadece gezip urun toplar, kasaya gider, PvP yapmaz) |

### 3.2 Kazanma ve Kaybetme

- **FFA Modu:** Son ayakta kalan oyuncu kazanir.
- **Takim Modu:** Tum takim uyeleri ayni kotayi paylasir. Kota karsilanamazsa tum takim elenir.
- **Oyun Bitisi (Ayarlanabilir):**
  - *Sinirsiz mod:* Son bir oyuncu/takim kalana kadar devam eder.
  - *Gun sinirli mod:* Belirlenen maksimum gun sayisina ulasilir (minimum 5 gun). En yuksek net servete sahip oyuncu/takim kazanir.
- **Gun siniri yoktur (sinirsiz modda).** Gunler ilerledikce kotalar artar, enflasyon yukselir, guvenlik sertlesir.
- **Kiyamet Gunu (Hard Cap):** Sinirsiz modda 30. gune ulasildiginda "Kiyamet Enflasyonu" ilan edilir. Kota astronomik bir rakama ($1.000.000) firlar ve oyun bir sonraki gunde dogal yoldan biter. Bu, sunucu belleginde birikme ve float deger sinirlarini asma riskini onler.

### 3.3 Raund ve Gün Süresi (Ayarlanabilir)
Bir oyun günü (raund), lobi lideri tarafından özelleştirilebilen tek bir sürekli zamanlayıcıya sahiptir (Varsayılan 10 Dakika, 5 ila 15 dakika arası ayarlanabilir). Bu süre iki ana aşamaya bölünür:
- **Açık Saatler (%80 Süre - Örn: İlk 8 Dk):** Sabah, Öğlen ve Akşam fazlarını içerir. AVM açıktır, dükkanlar yağmalanabilir, kasalardan kota ödemeleri yapılabilir.
- **Kapanış ve Gece Fazı (%20 Süre - Örn: Son 2 Dk):** AVM kapanış saatidir. Dükkanlar kepenk indirir, ışıklar söner, gece bekçileri devriye gezer ve kaçış bölgeleri (tahliye kapıları) aktifleşir.

### 3.4 Kota Zorlugu (Ayarlanabilir)

Mac basinda oyuncular kota zorlugunu secebilir (Kolay/Orta/Zor).

### 3.5 Günlük Akış Döngüsü

#### AÇIK SAATLER (%80 Süre)

**1. Sabah Fazı (İlk 1 Dk - Planlama):**
- AVM panosunda günlük enflasyon oranı, sektörel fiyat değişimleri ve özel olaylar duyurulur.
- Günlük görevler (rastgele 2-3 adet) yayınlanır.
- Oyuncular Kara Borsa deposuna (Black Market) giderek geceden kalan paralarıyla araba parçası değiştirme, yükseltme ve silah alımı işlemlerini yaparlar (Gece fazında kaçmayı başaran oyuncular bu fazda güvenle alışverişlerini yapar).

**2. Öğlen Fazı (Vahşi Yağma):**
- AVM kapıları açılır. Oyuncular reyonlardan mal toplar.
- PvP çarpışmalar, fırlatılabilir eşyalar, silahlar aktif hale gelir.
- Hırsızlık (gizli ceplere mal atma) ve standart güvenlik robotlarından kaçış mücadeleleri yaşanır.

**3. Akşam Fazı (Kasa Önü Safe Zone & Kota Ödeme):**
- Kasa bölgesi **güvenli bölge (safe zone)** ilan edilir: çarpışma hasarı, silahlar ve mal döktürme kapatılır (hafif fiziksel itişme aktiftir).
- **Sıra Atlama (QTE):** Kuyruktaki oyuncu önündekinin cebine sahte prop atarak alarmını çaldırıp onu sıradan attırabilir (10 sn cooldown mevcuttur).
- **Kotanın Ödenmesi:** Oyuncular kişisel kotalarını ve ortak kotayı tamamlamak için topladıkları malları kasada okutup ödemelerini yaparlar.

---

#### KAPANIŞ VE GECE FAZI (%20 Süre)

**4. Gece Fazı (Kapanış & Kaçış):**
- Raundun son %20'lik süresine girildiğinde hoparlörlerden AVM'nin kapandığına dair absürt anonslar çalmaya başlar.
- Mağazaların metal kepenkleri yavaş yavaş inerek kapanır; bu saatten sonra yeni ürün toplanamaz.
- AVM ışıkları kademeli olarak söner. Oyuncular farlarını açmak zorunda kalır.
- Standart robotların yerine fenerli, çok daha hızlı ve agresif **Gece Bekçileri** devriyeye başlar.
- **Kaçış ve Tahliye (Escape):** Haritanın belirli noktalarındaki kaçış bölgeleri (Merkez Yük Asansörü ve Otopark Çıkış Kapısı) aktifleşir.
- **Ceza ve Ganimet Kaybı:** Oyuncuların raund boyunca topladıkları (kasada ödemedikleri veya ceplerine attıkları) son dakika ganimetlerini koruyabilmeleri için sepetleriyle birlikte bu kaçış kapılarından başarıyla çıkış yapması gerekir. Süre bittiğinde AVM içinde kalan veya Gece Bekçilerine yakalanan oyuncuların tüm ganimetlerine el konur ve vergi cezası kesilir..

**Gece (Kara Borsa & Yatırım):**
- Araba parçası değiştirme/yükseltme
- Silah ve sabotaj ekipmanı alımı
- Trafo sabotajı (tüm oyuncular fakirse)
- Kat geçişi oylaması

---

## 4. UI/UX Tasarımı

### 4.1 HUD (Oyun Ici Arayuz) & Diegetic Göstergeler

HUD arayüzü, geleneksel 2D ekran göstergeleri yerine ağırlıklı olarak oyun dünyasındaki fiziksel nesnelere (diegetic) entegre edilmiştir:
- **Gidon Gösterge Paneli (Diegetic HUD):** Alışveriş arabasının gidonuna (tutma kolu) monte edilmiş küçük bir dijital ekrandır. Oyuncu arabayı sürerken sepetin içindekileri (isim ve simgelerini), her ürünün tekil dolar değerini ve sepetin güncel toplam değerini bu panelde gerçek zamanlı olarak takip edebilir.
- **Fiziksel Görev Kağıdı (Shopping List):** Arabanın ön sepetine mandalla tutturulmuş fiziksel bir alışveriş listesidir. Günlük görevlerin tamamlanma durumları bu kağıtta üstü çizili olarak gösterilir.
- **Ekran Göstergeleri (Minimal 2D HUD):** Ekranın köşelerinde sadece oyuncunun cep envanteri (maks 3 küçük slot), güncel el taşıma durumu (1 slot), Kasiyer Sabır Barı, günün bitmesine kalan süre (Round Timer) ve alarm seviyesi (Sarı/Turuncu/Kırmızı/Siyah) gösterilir.

### 4.2 Envanter Yonetimi
- **Sepet:** Toplanan urunler sepette gorunur ve sepette kalir. Sepetteki urunler gidondaki panelde değer ve isim olarak listelenir.
- **Cep (Gizli):** Calinti kucuk esyalar cebe atilir (maksimum 3 slot). Cep doldukca alarm sesi artar.

### 4.3 Lobby (Mac Onu)
- Oyuncuların seçtiği kozmetik karakter modelleri ve görünümleri görüntülenir.
- Kozmetik özelleştirme (karakter skin + araba skin + araba neon + absürt kornalar).
- Harita zorlugu secimi (Kolay/Orta/Zor) -> kota artis hizini belirler.
- Mac suresi secimi (Varsayılan 10 dk, 5-15 dk arası ayarlanabilir).
- Maksimum gun sayisi (Sinirsiz / 5 / 7 / 10).
- Mod secimi (FFA / 2v2 Takim).

### 4.4 Skor Tablosu (Mac Sonu)

- Her oyuncunun kazandigi para miktari
- Trol odulleri:
  - "Yilin En Buyuk Haini" (en cok ihanet eden) -> Sonraki maca Seytan Boynuzu sapkasi
  - "Dürüst Vatandas" (hic hirsizlik yapmayan) -> Sonraki maca %5 vergi indirimi
  - "Guvenlik Dostu" (en cok nezarethaneye dusen) -> Lockpick mini-oyunu kolaylasir
- Liderlik tablosu (Leaderboard): Yerel (arkadas) + Global (haftalik/aylik)

### 4.5 Iletisim Sistemi

- **Ping Sistemi:** Haritada isaretleme (git, suraya bak, tehlike)
- **Emote Sistemi:** Karakter karikatur balonlari (el hareketi, isaret etme, trol surati)
- **Sesli Sohbet:** Yaklasim esasli (proximity chat) — sadece yakin oyuncular duyar

### 4.6 Izleyici Modu (Spectator)

- **Free-cam:** Izleyiciler ozgur kamera ile maci izleyebilir
- **Oyuncu Takip:** Izleyiciler bir oyuncuyu secip onun perspektifinden izleyebilir

---

## 5. Kozmetik Karakterler ve Görünümler

Oyunda herhangi bir asimetrik sınıf mekaniği bulunmamaktadır. Oyun başladığında tüm oyuncular oynanış açısından tamamen eşit istatistiklere ve mekaniklere sahiptir. Karakter seçimi yalnızca görsel/kozmetik farklılıklardan ibarettir (Zengin Emekli, Çılgın Öğrenci, Ev Hanımı, Kaslı Fit vb. karakter modelleri ve kıyafetleri kozmetik olarak seçilebilir).

### 5.1 Ortak Oyuncu İstatistikleri
- **Maksimum Sürat:** Tüm oyuncular için standart hareket hızı.
- **Sepet Kapasitesi (Sanal Limit):** Başlangıçta 4 slot (Kara Borsa yükseltmeleriyle sırasıyla 6 ve 8 slot envanter kaydına çıkarılabilir). Fiziksel olarak sepet içine sığdırabildiği sürece istediği kadar mal yığabilir, ancak sadece envanter slot limiti kadarı kayıt altına alınabilir.
- **Cep Kapasitesi:** Standart 3 slot (yalnızca telefon, kozmetik ve takı gibi küçük boyutlu ürünler cebe konabilir).
- **El Taşıma Kapasitesi:** 1 slot (araba sürülmediği durumlarda oyuncu elinde 1 adet ürün taşıyabilir; eller doluyken araba sürülemez).
- **Hasar Sorumluluğu:** Çarparak reyon/raf kıran her oyuncu hasar bedelini ödemekle yükümlüdür. Eğer bir oyuncu başka bir oyuncu tarafından itilerek rafa çarptırıldıysa, hasar bedeli iten oyuncunun kişisel kotasına eklenir. Co-op modunda ise bu ceza doğrudan ortak takım bütçesine/kotasına yansıtılır.

---

## 6. Ekonomi Sistemi (Ozet)

### 6.1 Para Birimi
- **Tek para birimi (USD):** Her sey dolar uzerinden.

### 6.2 Cift Katmanli Kota
- **Kisisel Kota:** Her oyuncunun gun sonunda kasaya teslim etmesi gereken bireysel hedef
- **Ortak Kota:** Tum oyuncularin toplamda havuzda biriktirmesi gereken miktar
- Kota artis hizi mac basinda secilen zorluga gore belirlenir (Kolay/Orta/Zor)

### 6.3 Vergi Kacakcisi Korumasi (Bele sci Onlemi)
- Kasa aninda ortak kota dolmazsa, sistem **en cok paraya sahip olup ortak kotaya katki yapmayan** oyuncuyu tespit eder
- Bu oyuncu "Vergi Kacakcisi" ilan edilir, guvenlik dogrudan onu nezarethaneye atar ve parasi **zorla ortak kotaya** cekilir (**Tahsilat Limiti ve Ceza:** Sadece ortak kotayı tamamlayacak kadarlık eksik bakiye zorla çekilir ve üzerine bu çekilen eksik miktarın %10'u kadar ek vergi cezası kasaya gitmek üzere elinden alınır).
- Boylece bir oyuncunun kasitli olarak oyunu sabote etmesi engellenir
- Onceki gun hangi urun kategorisi cok yagmalandiya, ertesi gun o kategoride **fiyatlar dusur** (%40'a kadar)
- Az yagmalanan kategorilerin fiyati yukselir
- Bu sistem oyunculari her gun farkli reyonlara yonelmeye zorlar, meta-oyun cesitliligi saglar

### 6.3 Guc Dengesi
- **Luks Vergisi:** Onceki gun en cok kazanan oyuncudan kesinti + guvenlikler ona odaklanir
- **Sosyal Yardim:** En fakir oyuncuya indirim kuponu ve kacis kuponu

---

## 7. Alisveris Arabasi & Combat

### 7.0 Özel Karakter/Araba Kontrolcüsü (Custom Physics Controller) & Sürüş Etkileşimi
Standart s&box yürüme/karakter kontrol şablonları devre dışı bırakılmıştır. Oyuncu ve alışveriş arabası dinamik olarak birbirine kenetlenen veya ayrılan iki bağımsız fiziksel varlıktır:
- **Sol Tık Sürüş Etkileşimi (Hold to Drive):** Oyuncu, sepetin arkasındaki tutma koluna yaklaşıp **Sol Tık (LMB)** tuşuna bastığı ve basılı tuttuğu sürece arabayı sürüş moduna sokar.
  - IK el animasyonları (Inverse Kinematics) karakterin ellerini otomatik olarak tutma koluna kenetler.
  - Kamera yaya görünümünden (First Person veya omuz üstü Third Person), araba takip kamerasına (Third-Person Chase Cam) geçiş yapar.
  - Klavye yön girdileri (W-S-A-D) doğrudan araba tekerlek tork ve yönlendirme sistemlerine yönlendirilir.
- **Bırakma ve Serbest Momentum (Release & Glide):** Sol tık bırakıldığı anda karakter tutma kolunu bırakır. Araba motor gücü kesilir ancak araba anında durmaz; o anki hız ve yönüyle (fiziksel ataletiyle) kaymaya (glide) devam eder. Karakter ise anında serbest yaya moduna döner.
- **Tekerlek Fiziği:** Arabanın 4 tekerleği Source 2 fizik motorunda bağımsız süspansiyon ve sürtünme katsayılarına sahiptir. Ön tekerlekler yönlendirme (steering), arka tekerlekler tork/itici güç üretir.
- **Ağırlık Merkezi ve Atalet:** Sepete eklenen malların ağırlığı ve hacmi, arabanın kütle merkezini dinamik olarak yukarı ve ileri kaydırır. Bu durum, virajlarda savrulma (understeer/oversteer) ve devrilme risklerini doğrudan etkiler.
- **Drift/Kayma Fiziği:** Nitro ve el freni kullanılarak arabanın arka tekerleklerinin yol tutuşu geçici olarak düşürülebilir, bu sayede dar AVM köşelerinde kayarak dönme (drift) sağlanır.

### 7.1 Araba Parcalari (4 Adet)

| Parca | Islev |
|-------|-------|
| On Tampon (Bulldozer) | Kafa carpismalarinda kendi malini korur, karsiya cift hasar |
| V8 Motor (Nitro) | Kisa sureli hiz patlamasi |
| Manyetik Sepet | Sadece görüş hattındaki (Line of Sight) ve engelsiz yakın mesafedeki (maks 2m) küçük malları otomatik çeker |
| Egzoz Yag Tanki | Arkada kaygan yüzey bırakır (Maksimum 3 kullanım yüklüdür/charge-based, Kara Borsa'da geceleri doldurulur. Yağ lekesi yerde 15 saniye kalır) |

### 7.2 Parca Yonetimi
- Parcalar **her gece Kara Borsa'da degistirilebilir/yukseltilebilir**.
- Elindeki parcayi satip yenisini alabilirsin.

### 7.3 PvP ve Etkileşim Aksiyonları
- **Carpisma:** Araba ile rakibe vurma, mal dokturme.
- **Firlatma:** Sepetteki urunleri rakibe firlatma.
- **İterek Arabayı Fırlatma (LMB Release-Launch):** Oyuncu V8 Motor (Nitro) kullanarak tam hızla giderken Sol Tık'ı bıraktığı anda araba yüksek bir momentumla reyonun sonuna veya rakiplerin üzerine doğru fırlatılır. Boş giden araba çarptığı rakipleri savurur veya reyonları kırabilir.
- **Araba Çalma (Hijack):** Sahipsiz duran veya bir başkası tarafından fırlatılmış boş bir arabanın arkasına geçen herhangi bir rakip oyuncu Sol Tık basarak arabanın kontrolünü anında ele alabilir.
- **Silahlar:** Kara Borsa'dan alinabilen ozel saldiri araclari (havai fisek, sapan, boya tabancasi vb.)
- **Dokulen/Firlatilan Mal Secimi:** Arabadan dokulen veya firlatilan mallar "en son eklenen" degil, **rastgele** secilir. Bu sayede oyuncular pahali mallarini ucuz mal kalkani ardina gizleyemez.
- **Fizik ve Ağ Senkronizasyonu (Entity Limit):** Ağ senkronizasyonunu ve sunucu performansını korumak için haritada aynı anda en fazla 50 serbest fiziksel obje (yere dökülen mal vb.) bulunabilir. Ucuz mallar yere düştüğü anda görsel bir efektle hızlıca yok olurken, sadece yüksek değerli mallar fiziksel nesne olarak yerde kalır.
- **Prosedürel El Uzatma ve Fiziksel Kavrama (IK Grab System):** Karakterin bir ürünü raftan alırken, sepetinde taşırken veya fırlatırken kolu prosedürel Inverse Kinematics (IK) animasyonu ile nesneye doğru uzanır. Eşya karakterin elinde fiziksel bir prop olarak eklenir ve taşınır. Oyuncu arabayı sürmüyorsa elinde 1 adet mal taşıyabilir, ancak arabayı sürmek için sol tıkla gidonu kavradığında elindeki malı sepete atmak veya yere bırakmak zorundadır.
- **Fiziksel Sepet Yığınlaması (Physical Basket Stacking):** Sepet hacminin içinde bir tetikleyici alan (Trigger Box) bulunur. Sepet içine fiziksel olarak sığan mallar bu alana girdiğinde envantere (`InventoryComponent`) kaydedilir. Sığdığı sürece fiziksel limit yoktur ancak sepet sanal slot limiti (4/6/8) aşılırsa yeni bırakılan mallar envantere kaydedilmez. Sert çarpışma veya driftlerde sepet sınırını aşarak dışarı fırlayan fiziksel mallar envanterden otomatik olarak silinir.

### 7.4 Co-op Araba (2v2 Takim)
- **Surucu:** Araba yonu, nitro, drift
- **Nisanci:** Mal toplama, rakiplere firlatma, yuzey kirliligi
- **Hot-Swap (NetworkComponent Owner):** Surucu ve nisanci anlik rol degistirebilir. s&box'ta NetworkComponent Owner degistirilerek implemente edilir — Surucu ve Nisanci ayri NetworkComponent'lerdir, hot-swap aninda ownership el degistirir. **Momentum Korunumu:** Sürücü ve nişancı rol değiştirdiğinde arabanın anlık hızı, yönü, nitro kullanımı ve drift eğrisi kesintiye uğramadan yeni sürücüye pürüzsüzce aktarılır.
- **Savrulma (Animation-based):** Nisanci fizik motoruyla degil, surucunun drift skoru kritik esigi gectiginde tetiklenen bir kod animasyonu ile arabadan savrulur. Bu sayede netcode senkronizasyonu fizik tabanli cozumden cok daha stabil kalir.
- **Reyon Hasar Sorumluluğu:** Co-op modunda arabayla verilen reyon hasar cezaları, arabayı o esnada kimin sürdüğüne bakılmaksızın doğrudan ortak takım bütçesine/kotasına faturalandırılır.

---

## 8. Hirsizlik & Ceza Sistemi

- **Cep Slotu:** 3 adet gizli cep (yalnızca telefon, kozmetik ve takı gibi yüksek değerli küçük ürünler cebe konabilir)
- **Ses Fizigi:** Cepteki mal arttikca sangirti sesi yukselir, guvenligin dikkatini ceker
- **Alarm Seviyeleri (4) ve Soğuma:**
  - **Sari:** Uyari, guvenlik ilgilenmeye basladi
  - **Turuncu:** Kovalama basladi
  - **Kirmizi:** Takviye cagrildi, kat kilitlenmeye basladi
  - **Siyah:** Ozel tim geliyor, tum kat kilitlenir
  - *Soğuma Mekanizması:* Güvenliklerin görüş alanından çıktıktan sonra 20 saniye boyunca tespit edilmeden saklanılırsa alarm seviyesi kademeli olarak düşer.
- **Nezarethane ve Kapanış Cezası:** Yakalanan oyuncu hücreye atılır (1 dk). 
  - *Açık Saatlerde Yakalanma:* Nezarethanedeki oyuncunun sepetindeki ve cebindeki eşyalar koruma altındadır; diğer oyuncular tarafından kesinlikle çalınamaz veya dokunulamaz.
  - *Gece/Kapanış Fazında Yakalanma:* Gece bekçisi robotlarına yakalanan oyuncunun ceplerindeki ve sepetindeki tüm kaçak/toplanan mallara AVM yönetimi tarafından el konur (ganimet sıfırlanır) ve nezarethane süresi 1.5 dakikaya uzatılır.
- **Kefalet:** O günkü kişisel kotanın %50'sini ödeyerek anında çıkış (dinamik kefalet - yalnızca açık saatlerde kullanılabilir).
- **Lockpick & Kamu Hizmeti:** Mini-oyunlar ile cezayı kısaltma (Lockpick maymuncuk mini-oyunu veya AVM koridorlarını paspaslama mini-oyununu tamamlayarak nezarethane süresini azaltabilir).
- **Son Dakika Musamahasi:** Son 1 dk'da ceza 5 sn stun + mal kaybina donusur

---

## 9. Yapay Zeka NPC Ekosistemi

| NPC | Tehdit | Savunma |
|-----|--------|---------|
| **Kuponcu Amca (Hoarder)** | Reyonlari kurutur, onu kapatir | "Bedava dagitim" anonsu ile oyalama (Haritadaki belirli hoparlör panellerine fiziksel olarak basılarak tetiklenir; ücretsizdir ve 60 saniye cooldown süresi vardır) |
| **Yaramaz Cocuk (Gremlin)** | Arabadaki en pahali urunu calar | Çaldığı ürünü yakındaki rastgele bir yere fırlatıp kaçar. Oyuncu 5 saniye içinde çocuğa çarparsa çalmasını engelleyebilir veya fırlattığı yerden ürünü geri alabilir. |
| **Parfumcu Tezgahtar (Stunner)** | 3 sn korluk + hapsirma ile savrulma | Temas oncesi mudahale |
| **Guvenlik Robotu** | Tespit -> kovalama -> takviye cagirma | Gizli hareket, oyalama nesneleri |
| **Kasiyer** | Sabır barı %100 olunca sabrı taşıran oyuncuyu sıradan atar ve güvenli bölge dışına fırlatır | Sıra düzeni, rüşvet ve beklemeye saygı |

---

## 10. Dinamik AVM Mimarisi (Dikey İlerleme & Prosedürel Üretim)

### 10.1 Kat Yapısı ve Sonsuz Rastgelelik (Full Procedural Randomization)
Oyuna her sıfırdan girildiğinde katların sıralamaları, katlardaki mağaza/reyon dizilimleri, koridor yapıları ve satılan ürün kategorileri tamamen rastgele sıfırdan prosedürel olarak oluşturulur. Hiçbir oyun veya kat düzeni birbiriyle aynı olamaz, bu sayede oyuncuların her oyunda benzersiz bir AVM keşfetmesi sağlanır ve tekrar oynanabilirlik artırılır.

| Örnek Kat Seviyesi | Örnek Tema | Zorluk | Örnek Özellikler |
|-----|------|--------|------------|
| **Kat A** | Süpermarket & Ucuzluk | Kolay | Geniş koridorlar, yavaş hareket eden NPC'ler, temel gıda reyonları |
| **Kat B** | Teknoloji & Moda | Orta | Dar koridorlar, vitrin alarmları, yürüyen merdiven tuzakları |
| **Kat C** | Lüks Mücevherat | Zor | Kaygan mermer zeminler (buz pisti etkisi), lazer bariyerleri, robot köpek devriyeleri |

### 10.2 AVM Haritası ve Ana Bölümler (Required Zones)
Her prosedürel harita yapısı, stratejik olarak doğru yerlere yerleştirilmiş aşağıdaki 3 kesin ana bölümü içermek zorundadır:
- **Kasa Önü (Checkout / Safe Zone):** Haritanın merkezinde veya kat çıkış yollarına yakın konumlandırılır. Güvenli bölgedir (silah ve PvP hasarı engellenir). Kasiyerler ve sıra atlama QTE panelleri buradadır.
- **Nezarethane (Security / Jail):** Güvenlik devriyelerinin gözetiminde olan, yakalanan oyuncuların kapatıldığı stratejik demir parmaklıklı alandır.
- **Kara Borsa (Black Market Depot):** AVM'nin ücra, karanlık servis/depo alanlarında saklıdır. Yalnızca gece fazında aktifleşir.

### 10.3 s&box Prosedürel Altyapı ve Dengelemeler
- **s&box Implementasyonu:** Modüler prefab parçalar (reyonlar, kasalar, koridorlar, ana odalar) grid tabanlı bir sisteme kod (`ProceduralMallGenerator`) tarafından tamamen rastgele yerleştirilir.
- **NavMesh Yönetimi:** Raf kırılması veya yıkılabilir kolonların patlaması gibi durumlarda NavMesh anlık rebuild edilir (güvenlik robotlarının yolunu kaybetmemesi için).
- **Kota Dengeleme Güvencesi:** Prosedürel haritadaki malların toplam değeri, o günkü Ortak Kota miktarının en az 2.5 katı olacak şekilde algoritma tarafından güvence altına alınır (soft-lock engellenir).

### 10.4 Kat Geçişi ve Dikey İlerleme (Vertical Progression)
- **Kota ile Açılma Şartı:** Bir üst kata giden dikey geçiş yolları (yürüyen merdiven kepenkleri ve asansör kapıları) kilitlidir. Bu kilitlerin açılması için gün sonu Ortak Kotasının kasada %100 tamamlanması zorunludur. Kota tamamlandığı an kilitler otomatik olarak açılır.
- **Dikey İlerleme Araçları:**
  - **Yürüyen Merdivenler (Escalators):** Hızlı tırmanış sağlar ancak arıza durum etkilerine (debuff'lara) açıktır. Oyuncular birbirlerini basamaklardan aşağı itip düşürebilirler.
  - **Asansörler (Elevators):** Daha yavaş ve güvenlidir. Ancak asansör kabininin dar olması, 4 arabanın asansör içinde sıkışıp dar alan kavgalarına girmesine yol açar.

### 10.5 Urun Kategorileri (8+)
| Kategori | Bulundugu Kat | Deger Araligi |
|----------|--------------|--------------|
| Temel Gida | Kat A | Dusuk |
| Icecekler | Kat A | Dusuk |
| Temizlik Malzemesi | Kat A | Dusuk |
| Oyuncak & Kitap | Kat A | Orta |
| Kozmetik | Kat B | Orta |
| Elektronik | Kat B | Yuksek |
| Moda & Giyim | Kat B | Orta |
| Luks Esya | Kat C | Cok Yuksek |

---

## 11. Rastgele AVM Etkinlikleri (Kaos Faktoru)

| Etkinlik | Etki |
|----------|------|
| **Indirim Cadiri** | 5x degerli urunler, dar giriste arabalar sikisir |
| **Yuruyen Merdiven Arizasi** | Yürüyen merdivenlerin yönü değişir; üzerindeki oyunculara %50 hız azaltma ve geriye doğru itiş durum etkisi (debuff) uygulanır (Source 2 desync/ragdoll hatalarını önlemek için fiziksel yuvarlanma kaldırılmıştır). |
| **Grev (Isci Isyani)** | Temizlik grevi nedeniyle zemin deterjanla kaplanır; oyuncuların araba drift/savrulma oranları artar ve dönüş hassasiyetleri bozulur (sürtünmeyi sıfıra indiren buz pisti durum etkisi). |

---

## 12. Ittifak Sistemi (Ortak Hesap)

- Iki oyuncu arabalarini tokusturup anlasma butonuna basarak o gun icin hesaplarini birlestirebilir.
- Birbirlerine carptiklarinda mal dusmez, tuzaklardan etkilenmez.
- **Ortak Kota ve Ödeme:** İttifak kurulduğunda iki oyuncunun kişisel kotaları birleşerek tek bir ortak kota havuzu haline gelir (Ortak Kota = A + B). Gün sonunda kasa ödemesi bu ortak hesaptan tahsil edilir.
- **Bakiye Bölüşümü:** Gün sonu kasa ödemesi tamamlandıktan sonra ve ittifak otomatik olarak bittiğinde, ortak hesapta geriye kalan bakiye iki oyuncu arasında 50/50 eşit olarak paylaştırılır.
- *(Not: Ihanet mekanigi kaldirilmistir. Gelecekte tekrar degerlendirilirse, ihanet aninda haine kisa sureli nitro/bonus verilmesi onerilir.)*

---

## 13. Seyirci Cozumu: AVM Hayaleti

Elenen oyuncu "Tuketici Ruhu" olarak haritaya doner:
- Guvenlik kameralarini hackleyerek rakiplerin uzerine guvenlik salabilir
- Klima ile deterjanlari rakiplere dogru ufleyebilir
- **Ektoplazma Bari:** Sabotaj yapmak için enerji gereklidir, hayattaki oyuncuların mallarının kokusuyla dolar. **Kazanma Şartı:** Hayalet oyuncunun ektoplazma biriktirmesi için hayattaki bir oyuncunun arabasına yakın (3-5 metre) durması gerekir. **Geri Bildirim:** Bu esnada hayattaki oyuncu hafif bir rüzgar/fısıltı sesi duyar ve ekranının köşelerinde sis efekti (görüşü kapamayacak şekilde) oluşur, bu da arkasında bir hayalet olduğunu fısıldayarak onu tetikte tutar. Hayaletler fiziksel olarak görünmez ve etkisiz kalırlar.
- **Yeniden Dogus:** Bir rakibi basariyla sabote ederse, enkaz araba ile sahaya doner (Yeniden doğuş mekaniği maç boyunca her oyuncu için sadece 1 kez ile sınırlandırılmıştır ve 5. günden sonra tamamen devre dışı kalır).
- **Twitch Entegrasyonu (Yayıncı Modu):** Canlı yayın izleyici etkileşimi ve yayıncı modu kuralları sıfırdan tartışılmak üzere askıya alınmıştır (Gelecekte netleştirilecektir).

---

## 14. Gorev Sistemi (Hikaye & Anlati)

- **Gorev tabanli kisa hikaye modu.**
- Her gun rastgele **2-3 gorev** verilir:
  - "En az 3 kutu yumurta topla"
  - "Bir rakibine 3 kere carparak mal doktur"
  - "Guvenlik kamerasina yakalanmadan 1 urun cal"
  - "Kasiyerin sabir barini %80'in ustune cikarma" vb.
- Gorevler ekstra para kazandirir ve anlati cercevesini olusturur.

---

## 15. Ses Tasarimi (Karma Yaklasim)

- **Normal zaman:** Sakin AVM atmosferi (hafif müzik/müzak, anonslar, kalabalık uğultusu)
- **Aksiyon ani:** Kaotik tempoya geçiş (elektronik/rock, çarpışma efektleri, alarm)
- Toy & Toon görsel stiliyle uyumlu komik/abartılı ses efektleri (plastik ördek ciyaklamaları, metalik sepet çınlamaları, abartılı karikatür çarpışma sesleri)
- **Seslendirme / Absürt AVM Anonsları:** Hoparlörlerden absürt ve komik AVM kuralları anons edilir ("Sayın müşterilerimiz, reyonlarımızda kavga etmek yasaktır ama indirimler serbesttir"). Kasiyerlerin homurdanma ve oflama seslendirmeleri mevcuttur. Karakterler konuşmaz.

---

## 16. Sanat Stili: Toy & Toon

- Parlak, doygun renk paleti
- Abartili karakter tasarimlari (karikatur oranlari)
- Yumusak kenarlar, yuvarlak geometri
- Fiziksel aksiyonlar abartili (squash & stretch)
- Oyuncak/plastik malzeme hissi
- UI: Canli renkler, buyuk butonlar, eglenceli fontlar

---

## 17. Kozmetik Sistemi

### Kalicilik (Meta Progression)
- Run'lar arasi kalici olan sadece **kozmetik ogelerdir**.
- Oynanis ile ilgili her sey her run'da sifirlanir.

### Kozmetik Turleri
- **Karakter Skinleri:** Alternatif karakter görünümleri (Zengin Emekli, Çılgın Öğrenci, Ev Hanımı, Kaslı Fit vb. kozmetik kıyafet ve modelleri).
- **Araba Skinleri & Neonları:** Alışveriş arabası görünümleri, altına takılabilen neon ışıklar ve drift yaparken yerde renkli boya bırakan tekerlek izi efektleri.
- **Absürt Kornalar:** Korna çalındığında komik sesler çıkaran korna kozmetikleri.

### Kazanım Yöntemleri ve Cashback Sistemi
- **Sadakat Puanı (Cashback):** Run'lar sırasında toplanan standart USD doları her run sonunda sıfırlanır. Ancak harcanan veya kotaya ödenen her **$100 için oyunculara 1 AVM Sadakat Puanı (Cashback)** verilir. Bu puanlar kalıcıdır (meta-currency) ve run'lar arasında birikir.
- **Kozmetik Dükkanı:** Biriktirilen AVM Sadakat Puanları lobideki dükkanda harcanarak karakter skinleri, araba neonları ve kornalar satın alınabilir.
- **Başarılarla (Achievements):** Belirli basarilari yapinca özel skinler acilir (100 hirsizlik, 10 kere basarıyla kaçış vb.)

---

## 18. Kara Borsa Icerigi

Gece fazinda acilan Kara Borsa'da sunlar bulunur:

| Kategori | Icerik |
|----------|--------|
| **Araba Parcalari** | 4 parcanin alimi, satimi, yukseltmesi |
| **Silahlar** | Karisik sistem: bazilari tek kullanimlik (ucuz), bazilari kalici (pahali) — havai fisek, sapan, boya tabancasi, sersemletici bomba |
| **Sabotaj Ekipmani** | Kamera bozucu, anahtar kopyalama, alarm devre disi birakma |

*(Hisse senedi sistemi bu asamada planlanmamistir.)*

---

## 19. Multiplayer & Eslestirme

| Ozellik | Detay |
|---------|-------|
| **Ozel Oda** | Steam arkadaslarinla ozel oda kur |
| **Hizli Eslestirme** | Rastgele oyuncularla Quick Play |
| **Netcode** | s&box Sync, Rpc.*, Client-Side Prediction |
| **Bot** | Lobi dolana kadar basit dummy botlar (gezip urun toplar, kasaya gider, PvP yapmaz). **Botlar hata yapar:** guvenlige yakalanir, mal doker, toplama hizlari gercek oyuncu ortalamasina dengelenmistir. Boylece ortak kotayi yapay sekilde şişirmezler. |
| **Twitch Oylama Modu** | Yayıncı odalarında seyircilerin oyuna (indirimler, tuzaklar, robotlar) chat oylamasıyla müdahale etmesi |

---

## 20. Kontroller (Klavye + Fare)

| Aksiyon | Tus |
|---------|-----|
| Ileri/Geri | W / S |
| Don (Sol/Sag) | A / D |
| Nitro | Shift |
| Urun Topla | E |
| Urun Firlat | Sol Tik |
| Hirsizlik (Cebe At) | Q |
| Silah Ates | Sol Tik (silah varken) |
| Anlasma/Ittifak | F |
| Ihanet (Iptal) | F (basili tut) |
| Lockpick Mini-Oyun | Mouse hareketi |
| Harita/Envanter | Tab |
| Ping | Orta Tik |
| Emote Menu | T |
| Sesli Sohbet | V (basili tut) |
| Kamerayi cevir | Mouse |

- **Tus yeniden atama (rebindable keys)** desteklenecek.

---

## 21. Egitim Modu (Tutorial)

- **Kisa video/ekran:** Baslangicta bir kac ekranla kontroller ve temel mekanikler anlatilir.
- Direkt maca atilir, oyuncu keşfederek ogrenir.

---

## 22. Erisilebilirlik (Kapsamli)
- Tus yeniden atama
- Renkoru modu (colorblind mode)
- Kamera sarsintisi kapatma
- Altyazi (subtitles)
- Kontrast modu
- Buyuk UI secenegi
- Sesli betimleme (opsiyonel)

---

## 23. Mod / Workshop Desteği

- **MVP'de yok.** Oyuncularin kendi karakter, araba ve haritalarini yapip paylasmasi ileriki asama icin degerlendirilecek.
- s&box'un dogal Workshop destegi ileride kullanilabilir.

---

## 24. Holding Company / Sirket Sistemi

- **Kesildi (su anlik).** MVP sonrasi tekrar degerlendirilecek.
- Mevcut ekonomi sistemi (kota, enflasyon, kara borsa) holding olmadan da calisir durumda.

---

## 25. Geliştirme Yol Haritası (2 Yıllık Detaylı Plan — 24 Ay / 96 Hafta)

> [!NOTE]
> **Kalite Kapısı ve İterasyon Esnekliği:** Eğer herhangi bir sistem planlanan aşamada hedeflenen eğlence, kararlılık ve görsel/işitsel kalite standardına ulaşamazsa, o sistemin geliştirme ve cila süresi bir sonraki aya taşarak uzatılabilir. Bu durumlarda ek cila ve iterasyon haftaları otomatik olarak devreye girer.

Aşağıdaki plan, en temel sistemlerden (temel motor kurulumu, fizik, ağ) en üst seviye mekaniklere (Twitch entegrasyonu, 2v2 co-op, cila) doğru sıralanmıştır. Her ay geliştirilecek olan s&box C# Bileşenleri (Components) ve sorumlulukları belirtilmiştir.

### YIL 1: Temel Motor, Fizik, Ağ ve Çekirdek Oynanış Döngüsü

#### AY 1: Proje Kurulumu, Girdi (Input) ve Temel Sürüş Altyapısı
* **Oluşturulacak C# Bileşenleri:**
  - `PlayerInputRouter`: Klavye/fare girdilerini yakalar, Sol Tık (LMB) basılı tutulma durumunu denetler; sepet kolu etkileşim menziline girdiğinde tutma ve yaya/sürüş girdileri arası dinamik yönlendirmeyi yönetir.
  - `ShoppingCartPhysics`: LMB basılıyken motor torku ve steering uygulayarak arabayı hareket ettirir, LMB bırakıldığında gücü keser ve momentum kaymasını denetler.
* **Haftalık Plan:**
  - **Hafta 1:** s&box motoru proje dizini kurulumu ve git entegrasyonu.
  - **Hafta 2:** `PlayerInputRouter` girdileri yakalama, Sol Tık sürüş/yaya geçiş mantığı.
  - **Hafta 3:** `ShoppingCartPhysics` ile araba sürtünme, ivmelenme ve momentum kayması (glide) fiziği.
  - **Hafta 4:** Temel HUD arayüz iskeletinin oluşturulması ve ekran ölçekleme testleri.

#### AY 2: Fizik Motoru Kalibrasyonu ve Çarpışma Mekanikleri
* **Oluşturulacak C# Bileşenleri:**
  - `ShoppingCartPhysics` (Geliştirilmiş): Süspansiyon, sürtünme, drift ve ağırlık merkezi hesaplamalarını yönetir.
  - `DestructibleShelf`: Reyon raflarının darbe kuvvetine göre kırılmasını ve malların saçılmasını yönetir.
  - `CollisionDmgTracker`: Çarpışma hızına göre hasar bedelini hesaplar, iten oyuncuyu tespit ederek faturayı ona yazar.
* **Haftalık Plan:**
  - **Hafta 5:** `ShoppingCartPhysics` kütle, hızlanma ve drift (savrulma) fiziği ayarlamaları.
  - **Hafta 6:** Arabalar arası kafa kafaya ve yandan fiziksel çarpışma tepkileri.
  - **Hafta 7:** `CollisionDmgTracker` reyon hasarı ve itilerek çarptırılan oyuncunun tespiti.
  - **Hafta 8:** `DestructibleShelf` fiziksel olarak kırılması ve reyon yıkılma animasyonları.

#### AY 3: Ağ Senkronizasyonu ve Client-Side Prediction (Netcode)
* **Oluşturulacak C# Bileşenleri:**
  - `NetworkSyncHelper`: Oyuncu pozisyonlarını, sürüş durumlarını ve çarpışma verilerini ağda senkronize eder.
* **Haftalık Plan:**
  - **Hafta 9:** s&box NetworkComponent altyapısının kurulması ve oyuncu pozisyonlarının senkronizasyonu.
  - **Hafta 10:** `NetworkSyncHelper` çarpışmalarda Client-Side Prediction (istemci tarafı tahmin) sistemini kurar.
  - **Hafta 11:** Rpc.* çağrılarının optimizasyonu ve gecikme telafisi (lag compensation).
  - **Hafta 12:** Ağ paket boyutlarının küçültülmesi ve ilk kararlılık (desync) stres testi.

#### AY 4: Çekirdek Yağma Döngüsü ve Sepet Envanteri
* **Oluşturulacak C# Bileşenleri:**
  - `LootItem`: Reyonlardaki toplanabilir ürünlerin fiyat, kategori, ağırlık ve durumunu saklar.
  - `InventoryComponent`: Sepet tetikleme alanı (Trigger Box) etkileşimlerini, sepet slot limitlerini (4/6/8), cep slotlarını (max 3 küçük ürün) ve el taşıma durumunu yönetir.
  - `HandReachIK`: Ürün toplarken, fırlatırken ve elinde taşırken karakterin kolunu Inverse Kinematics (IK) ile ürüne kilitler.
* **Haftalık Plan:**
  - **Hafta 13:** `LootItem` toplanabilir "Entity" mantığı ve kategorilerin veri yapısı.
  - **Hafta 14:** `InventoryComponent` sepet trigger tespiti, sanal slot limit kontrolü ve HUD entegrasyonu.
  - **Hafta 15:** `HandReachIK` el ile taşıma (1 slot), araba kavrama geçiş animasyonları ve prop elde tutma sistemi.
  - **Hafta 16:** Hırsızlık cep slotu (max 3 küçük ürün), cebe atma mekaniği ve ses eşiği sangırtı artış hesapları.

#### AY 5: Akşam Fazı (Kasa Önü) Safe Zone ve Sıra Atlama Mekanikleri
* **Oluşturulacak C# Bileşenleri:**
  - `CheckoutZone`: Kasa bölgesine giren arabaların hasar ve silahlarını kapatır, safe zone durumuna sokar.
  - `QueueQTEController`: Sıra atlama esnasında E tuşuyla tetiklenen mini oyunu ve cooldown sürelerini yönetir.
  - `CashierAI`: Kasiyerin sabır barını yönetir, sabır taştığında oyuncuyu sıradan dışarı fırlatma tetikleyicisini çalıştırır.
* **Haftalık Plan:**
  - **Hafta 17:** `CheckoutZone` akşam kasa önü safe zone hasar engelleme mantığı.
  - **Hafta 18:** `QueueQTEController` sıra atlama mini oyunu QTE tasarımı ve 10 sn cooldown kontrolü.
  - **Hafta 19:** `CashierAI` sabır barı artış/azalış mantığı ve sabır taşınca oyuncuyu sıradan dışarı fırlatma tetiklemesi.
  - **Hafta 20:** Kasiyere rüşvet ($100) vererek sabır barını düşürme arayüzü ve entegrasyonu.

#### AY 6: Temel Yapay Zeka (NPC) Altyapısı ve Güvenlik Robotu
* **Oluşturulacak C# Bileşenleri:**
  - `GuardRobotAI`: Güvenlik robotunun devriye, kovalama, alarm seviyesi artırma ve yakalama davranışlarını yönetir.
* **Haftalık Plan:**
  - **Hafta 21:** NavMesh üzerinde hareket eden temel NPC yapay zeka iskeleti (Behavior Tree).
  - **Hafta 22:** `GuardRobotAI` oyuncuyu görüş açısına girdiğinde kovalama ve devriye rotaları.
  - **Hafta 23:** Güvenlik Robotu alarm seviyeleri (Sarı/Turuncu/Kırmızı/Siyah) entegrasyonu.
  - **Hafta 24:** Hasar gören raflar sonrası dinamik NavMesh rebuild (yapay zekanın engellerin etrafından dolaşması).

#### AY 7: Dinamik Ekonomi, Kota ve Raund Zamanlayıcı Sistemleri
* **Oluşturulacak C# Bileşenleri:**
  - `GameEconomyManager`: Enflasyon, fiyat değişimleri, kotalar, vergi kaçakçıları ve sosyal yardımları hesaplar.
  - `RoundTimerManager`: Raund süresini (Açık Saatler ve Kapanış/Gece Fazı) yönetir, kepenk inme ve asansör/kaçış kapı kilit tetikleyicilerini koordine eder.
* **Haftalık Plan:**
  - **Hafta 25:** Kişisel ve Ortak kota barlarının HUD'a entegrasyonu ve gün sonu kota kontrolü algoritması.
  - **Hafta 26:** Arz-talep bazlı fiyat dalgalanması (önceki gün çok çalınan malın ertesi gün fiyatının düşmesi).
  - **Hafta 27:** `GameEconomyManager` vergi kaçakçısı tespiti ve en zengin oyuncunun parasını zorla ortak kotaya çekme mantığı.
  - **Hafta 28:** `RoundTimerManager` ile son %20'lik sürede AVM ışıklarını kısma, kepenk indirme ve kaçış kapılarını aktif etme entegrasyonu.
  - **Hafta 26:** Arz-talep bazlı fiyat dalgalanması (önceki gün çok çalınan malın ertesi gün fiyatının düşmesi).
  - **Hafta 27:** `GameEconomyManager` vergi kaçakçısı tespiti ve en zengin oyuncunun parasını zorla ortak kotaya çekme mantığı.
  - **Hafta 28:** Güç Dengesi mutatörleri (Lüks Vergisi ve Sosyal Yardım kuponları).

#### AY 8: Nezarethane, Dinamik Kefalet ve Kurtulma Mini Oyunları
* **Oluşturulacak C# Bileşenleri:**
  - `JailZone`: Nezarethaneye atılan oyuncuların sürelerini ve kurtulma durumlarını takip eder.
  - `LockpickMiniGame`: Fare hareketleriyle maymuncuk mini oyununu yönetir.
  - `SweeperMiniGame`: Yerdeki lekeleri paspaslama mini oyununu yönetir.
* **Haftalık Plan:**
  - **Hafta 29:** `JailZone` yakalanan oyuncunun 1 dakikalığına nezarethaneye ışınlanması.
  - **Hafta 30:** `LockpickMiniGame` fare hareketiyle kilit açma mekaniği.
  - **Hafta 31:** `SweeperMiniGame` paspaslama mini oyunu ve yerdeki lekeleri temizleyerek süreyi kısaltma mekaniği.
  - **Hafta 32:** Dinamik kefalet sistemi (o günkü kişisel kotanın %50'sinin ödenerek anında çıkılması).

#### AY 9: Gelişmiş Yapay Zeka NPC'leri (Komedi & Sabotaj)
* **Oluşturulacak C# Bileşenleri:**
  - `HoarderAI`: Reyonları kurutan rakip kuponcu amca yapay zekası.
  - `GremlinAI`: Arabalardan eşya çalan yaramaz çocuk yapay zekası.
  - `StunnerAI`: Oyuncuları kör edip savuran parfümcü tezgahtar yapay zekası.
* **Haftalık Plan:**
  - **Hafta 33:** `HoarderAI` reyonlardaki ürünleri toplayıp kendi sepetine doldurma davranışı.
  - **Hafta 34:** `GremlinAI` sepetten mal çalma ve çarpıldığında malı düşürme davranışı.
  - **Hafta 35:** `StunnerAI` parfüm sıkarak oyuncunun ekranını bulandırma ve kontrolünü zorlaştırma davranışı.
  - **Hafta 36:** NPC'lerin birbirleriyle olan fiziksel ve yapay zeka etkileşimlerinin dengelenmesi.

#### AY 10: Seyirci Çözümü (AVM Hayaleti / Spectator)
* **Oluşturulacak C# Bileşenleri:**
  - `SpectatorGhost`: Elenen oyuncunun hayalet formunda gezip ektoplazma biriktirmesini ve tuzak kurmasını sağlar.
  - `HackableCamera`: AVM kameralarının hayaletler tarafından hacklenerek güvenlik uyarısı vermesini sağlar.
* **Haftalık Plan:**
  - **Hafta 37:** Elenen oyuncuların `SpectatorGhost` formunda izleyici moduna (free-cam / oyuncu takip) geçişi.
  - **Hafta 38:** Ektoplazma barı biriktirme (hayattaki oyuncuların mallarını koklama) sistemi.
  - **Hafta 39:** `HackableCamera` sabotajı ve klima tuzaklarını açarak deterjan püskürtme sabotajları.
  - **Hafta 40:** Başarılı sabotaj sonrası enkaz araba ile maçta 1 kez yeniden doğuş sistemi (gün 5 öncesi).

#### AY 11: Çok Oyunculu Lobi ve Arkadaş Eşleştirme Sistemleri
* **Oluşturulacak C# Bileşenleri:**
  - `LobbyController`: Lobi kurulması, oyuncu davetleri, kozmetik seçimleri ve hazır durumlarını yönetir.
  - `DummyBotAI`: Oyuncular yerine lobi doldurmak için gezen temel bot yapay zekası.
* **Haftalık Plan:**
  - **Hafta 41:** `LobbyController` Steam Arkadaş Daveti (Özel Oda) arayüz tasarımı ve oda kurma kodları.
  - **Hafta 42:** Hızlı Eşleştirme (Quick Play) matchmaking algoritması.
  - **Hafta 43:** `DummyBotAI` tasarımı (PvP yapmayan, sadece gezip kota doldurmaya çalışan test botları).
  - **Hafta 44:** Botların rastgele hata yapması (güvenliğe takılması, mal dökmesi) ve kota katkı dengesi.

#### AY 12: Yıl Sonu Alpha Sürüm Kararlılık ve Stres Testleri
* **Oluşturulacak C# Bileşenleri:**
  - `AlphaTestTelemetry`: Test esnasındaki ping, FPS, gecikme ve desync durumlarını izleyen analiz bileşeni.
* **Haftalık Plan:**
  - **Hafta 45:** Tüm birinci yıl mekaniklerinin (Sürüş, Netcode, Kasa Önü, Yapay Zeka, Ekonomi) birleşik testi.
  - **Hafta 46:** 4 gerçek oyuncu ile yüksek gecikmeli ağ ortamlarında alpha playtest oturumları.
  - **Hafta 47:** `AlphaTestTelemetry` verileri ile tespit edilen senkronizasyon hatalarının giderilmesi.
  - **Hafta 48:** Alpha sürüm verilerine göre fizik motoru çarpışma eşiklerinin güncellenmesi.

---

### YIL 2: Meta Sistemler, Prosedürel Üretim, Co-op ve Lansman Hazırlığı

#### AY 13: Prosedürel Harita Üretimi (AVM Kat Yapısı)
* **Oluşturulacak C# Bileşenleri:**
  - `ProceduralMallGenerator`: Modüler oda, reyon ve koridor prefab'larının grid tabanlı rastgele dizilimini yönetir.
* **Haftalık Plan:**
  - **Hafta 49:** Prefab bazlı modüler oda ve koridor yerleşim algoritması (Grid Builder).
  - **Hafta 50:** Kat temalarının dinamik ve rastgele belirlenmesi.
  - **Hafta 51:** `ProceduralMallGenerator` ile haritadaki malların toplam değerini denetleyen "Kota Dengeleme Güvencesi" (Kota * 2.5).
  - **Hafta 52:** Kota tamamlandığında asansör kapıları ve yürüyen merdiven kilitlerinin açılmasını sağlayan tetikleyici sistemlerin entegrasyonu.

#### AY 14: Dinamik Çevre Tuzakları ve Durum Etkileri (Debuffs)
* **Oluşturulacak C# Bileşenleri:**
  - `DebuffTrap`: Yürüyen merdiven ve grev zeminlerinde oyuncuya uygulanan durum etkilerini (debuff) yönetir.
* **Haftalık Plan:**
  - **Hafta 53:** `DebuffTrap` ile yürüyen merdiven arıza tuzağı: %50 yavaşlama ve geriye doğru itiş durum etkisi.
  - **Hafta 54:** `DebuffTrap` ile temizlik grevi tuzağı: sürtünmeyi sıfıra indiren buz pisti durum etkisi.
  - **Hafta 55:** Muhtelif tuzaklar (vitrin alarmları, lazer bariyerler, kaygan mermer zeminler).
  - **Hafta 56:** Tuzakların ağ üzerindeki senkronizasyon testleri (fizik yerine durum efekti kullanılarak).

#### AY 15: Kara Borsa ve Araba Parça/Modifikasyon Sistemi
* **Oluşturulacak C# Bileşenleri:**
  - `BlackMarketManager`: Gece fazında parça yükseltmelerini ve alış/satış işlemlerini yönetir.
  - `VehiclePart`: Alışveriş arabasına takılan modifikasyonların (Bulldozer, Yağ Tankı, V8, Manyetik) aktif/pasif durumlarını yönetir.
* **Haftalık Plan:**
  - **Hafta 57:** `BlackMarketManager` gece arası güvenli lobi arayüz tasarımı ve envanter yükseltme entegrasyonu.
  - **Hafta 58:** Ön Tampon (Bulldozer) ve Egzoz Yağ Tankı mekaniklerinin kodlanması.
  - **Hafta 60:** V8 Motoru (Nitro) ve Manyetik Sepet (otomatik çekim) mekaniklerinin kodlanması.
  - **Hafta 60:** Satın alınan yükseltmelerin alışveriş arabasına görsel ve fiziksel olarak eklenmesi.

#### AY 16: Kara Borsa Silahları ve Sabotaj Ekipmanları
* **Oluşturulacak C# Bileşenleri:**
  - `WeaponItem`: Havai fişek, sapan, boya tabancası gibi silahların atış yönü, isabeti ve cephane/durumunu yönetir.
* **Haftalık Plan:**
  - **Hafta 61:** Havai fişek, sapan ve boya tabancası gibi fırlatılabilir silah mekanikleri.
  - **Hafta 62:** Sabotaj araçları (kamera bozucu, anahtar kopyalama cihazları).
  - **Hafta 63:** `WeaponItem` ağ üzerindeki atış ve isabet tahmin kodlamaları.
  - **Hafta 64:** Silahların ve sabotaj ekipmanlarının Kara Borsa ekonomisine entegrasyonu.

#### AY 17: Co-op Modu (Driver & Gunner)
* **Oluşturulacak C# Bileşenleri:**
  - `CoopVehicleController`: Sürücü ve Nişancının tek bir arabaya olan girdilerini yönetir, drift değerlerini hesaplar.
* **Haftalık Plan:**
  - **Hafta 65:** 2v2 modda tek alışveriş arabasını iki oyuncunun kontrol etmesi altyapısı.
  - **Hafta 66:** `CoopVehicleController` NetworkComponent Owner hot-swap mekanizmasının s&box üzerinde kodlanması.
  - **Hafta 67:** Nişancı oyuncunun drift esnasında arabadan savrulma kod animasyonları.
  - **Hafta 68:** Co-op sürüş testleri ve eşzamanlı iki oyuncu girdi (input) çakışması çözümleri.

#### AY 18: Sürüş Fiziği Kalibrasyonu, Ağ Cilası ve Performans Optimizasyonları (Buffer)
* **Oluşturulacak C# Bileşenleri:**
  - `PhysicsPolishManager`: Araba çarpışmaları, momentum kaymaları ve IK el kavramalarının ağ üzerindeki gecikmelerini (lag) optimize eden cila bileşeni.
* **Haftalık Plan:**
  - **Hafta 69:** Sol Tık (LMB) sürüş ve bırakma momentumunun farklı ağ gecikmelerinde (client/server prediction) pürüzsüzleştirilmesi.
  - **Hafta 70:** Çarparak reyon kırma ve hasar faturası belirleme sisteminin (CollisionDmgTracker) performans optimizasyonları.
  - **Hafta 71:** `PhysicsPolishManager` ile aynı anda sahada olan 50+ fiziksel eşyanın ağ paket boyutlarının küçültülmesi ve ağ cila testleri.
  - **Hafta 72:** Co-op hot-swap momentum aktarımının ve arabadan savrulma animasyonlarının gecikmeli sunuculardaki kalibrasyon cilası.

#### AY 19: Kozmetikler ve Kalıcı İlerleme (Meta Progression)
* **Oluşturulacak C# Bileşenleri:**
  - `CosmeticsManager`: Oyuncuların kilidini açtığı skinleri bulutta saklar ve lobide yükler.
* **Haftalık Plan:**
  - **Hafta 73:** Karakter ve Araba skinlerinin (boyalar, bayraklar, alt neonlar) veri yapısının kurulması.
  - **Hafta 74:** Drift esnasında yerde renkli iz bırakan tekerlek izi efektlerinin kodlanması.
  - **Hafta 75:** Oyun içi para ve Steam Başarımları (Achievements) ile kozmetik açma sistemi.
  - **Hafta 76:** Bulut sunucu (Cloud Save) entegrasyonu ile kozmetik sahipliğinin saklanması.

#### AY 20: Absürt Ses Tasarımı ve Hoparlör Anonsları
* **Oluşturulacak C# Bileşenleri:**
  - `AbsurdAudioManager`: AVM anonslarını, ciyaklama seslerini ve dinamik arka plan müzik geçişlerini yönetir.
* **Haftalık Plan:**
  - **Hafta 77:** Komik ses efektleri (plastik ördek ciyaklaması, metalik sepet çınlamaları, Toon çarpışma sesleri).
  - **Hafta 78:** Hoparlörlerden duyulacak absürt AVM anonslarının stüdyo/yapay zeka ses kayıtlarının entegrasyonu.
  - **Hafta 79:** Kasiyerlerin homurdanma, oflama seslerinin oyun durumuna göre tetiklenmesi.
  - **Hafta 80:** `AbsurdAudioManager` ile dinamik müzik sistemi: normal zaman ve aksiyon geçişleri.

#### AY 21: HUD Diegetic Dönüşümü ve Performans Optimizasyonu
* **Oluşturulacak C# Bileşenleri:**
  - `DiegeticHUD`: Oyun içindeki ışık, siren ve el terminali ekranlarını yöneterek klasik HUD öğelerini azaltır.
* **Haftalık Plan:**
  - **Hafta 81:** Alarm barlarının kaldırılarak AVM ışık renklerine (Sarı, Turuncu, Kırmızı) ve siren temposuna dönüştürülmesi.
  - **Hafta 82:** Görev listesinin diegetic hoparlör anonsları ve `DiegeticHUD` el terminali ekranına entegre edilmesi.
  - **Hafta 83:** Prosedürel haritalarda render optimizasyonları (Occlusion Culling).
  - **Hafta 84:** Çok sayıda fizik nesnesi (50 serbest obje) varken kare hızının (FPS) sabit tutulması.

#### AY 22: Kapalı Beta (Closed Beta) ve İnce Dengelemeler (Fine-Tuning)
* **Oluşturulacak C# Bileşenleri:**
  - `BetaTelemetry`: Kapalı beta oyuncularının oyun içi istatistiklerini (kullanılan sınıflar, ödenen kotalar vb.) toplar.
* **Haftalık Plan:**
  - **Hafta 85:** Sınırlı sayıda davetiye ile Kapalı Beta sürecinin başlatılması.
  - **Hafta 86:** Reyon hasar faturalarının ve Kara Borsa fiyat dengelerinin oyunculardan alınan verilere göre dengelenmesi.
  - **Hafta 87:** Kasiyer sabır barı artış hızları ve rüşvet miktarlarının dengelenmesi.
  - **Hafta 88:** `BetaTelemetry` verileri doğrultusunda hatalı çalışan sistemlerin iterasyon/süre esnetme süreçlerine alınması.

#### AY 23: Açık Beta (Open Beta) ve Steam API Entegrasyonları
* **Oluşturulacak C# Bileşenleri:**
  - `SteamAPIManager`: Steam başarımları, arkadaş davetleri ve liderlik tabloları entegrasyonlarını yönetir.
* **Haftalık Plan:**
  - **Hafta 89:** Açık Beta sürümünün tüm oyunculara açılması ve sunucu stres testi.
  - **Hafta 90:** Steam Liderlik Tabloları (Leaderboards) ve arkadaş davet API'larının bağlanması.
  - **Hafta 91:** `SteamAPIManager` ile başarımların tetiklenmesi.
  - **Hafta 92:** Hata raporlama (bug report) ve otomatik çökme günlüğü (crash dump) sistemlerinin kurulması.

#### AY 24: Lansman Hazırlığı, Hata Ayıklama ve Yayınlama
* **Oluşturulacak C# Bileşenleri:**
  - `LaunchReleaseManager`: Oyun içi marketleri ve global sunucu durumlarını denetler.
* **Haftalık Plan:**
  - **Hafta 93:** Açık beta sürecinden gelen kritik bugların temizlenmesi (Lansman öncesi son cila).
  - **Hafta 94:** `LaunchReleaseManager` ile kozmetik market testleri ve global sunucu kapasite artırımı.
  - **Hafta 95:** Tanıtım materyalleri, ekran görüntüleri ve Steam mağaza sayfası güncellemeleri.
  - **Hafta 96:** Black Friday: The Game — Resmi Yayınlanma (Lansman).

## 27. Tasarim Riskleri ve Cozumleri (Playtest Oncesi)

### Risk 1: Fizik ve Ağ Senkronizasyonu (s&box Kısıtlamaları ve Kaos Yükü)
**Sorun:** Öğlen fazındaki çarpışmalar, kırılan reyonlar ve fırlatılan ürünler nedeniyle etrafa saçılan yüzlerce fiziksel nesnenin 4 oyuncu arasında senkronizasyonu sunucuda devasa gecikmelere (desync/lag) yol açar.
**Cözüm:** Haritada aynı anda en fazla 50 serbest fiziksel nesne sınırı (Entity Limit) uygulanır. Ucuz mallar düştüğü anda görsel efektle kaybolurken, sadece yüksek değerli mallar fiziksel nesne olarak yerde kalır.

### Risk 2: Çarpışma ve İterek Reyon Kırma Suistimali (Griefing) Açıkları
**Sorun:** Bir oyuncunun rafları kırdığında hasar bedelini ödemesi kuralı, başka bir oyuncu onu itip raflara çarptırdığında haksız yere iflasına neden olabilir.
**Cözüm:** Hasar bedeli kuralına "iterek çarptırma tespiti" eklenerek hasar faturası çarptırılan oyuncuya değil, onu iten oyuncuya kesilir. Oyuncuların hız ve çarpışma mekanikleri arasındaki denge düzenli testlerle izlenir.

### Risk 3: Kasa Önü Kilitlenme ve Trolleme Mantığı
**Sorun:** QTE (Sıra Atlama) başarısız olduğunda kasiyer sabrının taşarak kasayı kilitlemesi, parası olmayan toksik bir oyuncu tarafından kasıtlı olarak kasanın sürekli kilitli tutulması ve dürüst oyuncuların elenmesi amacıyla suistimal edilebilir.
**Cözüm:** Kasiyerin sabrı taştığında kasa tamamen kilitlenmez; sadece sabrı taşıran oyuncu sıradan atılır ve kısa süreliğine güvenli bölgenin dışına fırlatılır.

### Risk 4: Ekonomi ve İlerleme Eğrisinde Kırılma (Soft-Lock) İhtimali
**Sorun:** Kotalar üstel artarken enflasyon ve arz/talep dalgalanmaları nedeniyle oyuncuların tüm haritayı toplasalar bile kotayı ödeyemeyecekleri bir matematiksel tıkanma (soft-lock) yaşanabilir.
**Cözüm:** Prosedürel harita üretilirken haritaya yerleştirilen malların toplam değeri, o günkü Ortak Kota miktarının her zaman en az 2.5 katı olacak şekilde algoritma tarafından güvence altına alınır.

### Risk 5: "AVM Hayaleti" (Spectator) Sonsuz Döngüsü
**Sorun:** Elenen oyuncuların sabote edip enkaz araba ile sahaya dönmesi, son iki oyuncu arasında sonsuz bir ölüm-dirilim döngüsüne (stalemate) yol açabilir.
**Cözüm:** Yeniden doğuş (dirilme) mekaniği maç boyunca her oyuncu için sadece 1 kez ile sınırlandırılmıştır ve 5. günden sonra tamamen devre dışı bırakılır.

### Risk 6: Bilişsel Yük ve UI/UX Karmaşası
**Sorun:** Günlük para, sepet, cep, çift katmanlı kota, sabır barı, alarm seviyesi gibi birçok parametrenin HUD'da sayısal/metinsel gösterilmesi casual parti oyuncuları için aşırı bilgi yüklemesine neden olur.
**Cözüm:** Görev listesi ve alarm durumu gibi ögeler diegetic (oyun dünyasına entegre) görsel ve işitsel geri bildirimlere dönüştürülmüştür. Örneğin alarm seviyesi metinsel bar yerine AVM'nin acil durum ışık rengi ve sirenin temposuyla anlatılır.

### Risk 7: Co-op Araba Network Senkronizasyonu
**Sorun:** Hareket eden aracin uzerindeki oyuncuyu fizikle tutmak desync yaratir.
**Cozum:** Savrulma drift skoru kritik esigi gectiginde tetiklenen kod animasyonu. Hot-Swap NetworkComponent Owner degisimi ile implemente edilir.

### Risk 8: Yürüyen Merdiven ve Çevre Tuzakları Network Senkronizasyonu
**Sorun:** Source 2 motorunda merdivenlerden ters yuvarlanma gibi ağır fiziksel ragdoll durumları 4 oyuncu arasında senkronizasyon kayıplarına ve takılmalara yol açar.
**Cözüm:** Fiziksel yuvarlanma iptal edilmiş; bunun yerine merdivene basıldığında veya grev anında oyunculara %50 yavaşlama ve kayma gibi durum etkileri (debuff) uygulanarak netcode performansı korunmuştur.

---

### Loophole 1: Reyon Kirma Suistimali (Griefing)
**Acik:** Toksik oyuncu 3. Kattaki luks raflari bilerek kırarak rakiplerin kota odemesini imkansiz hale getirebilir.
**Cozum:** Kirilan her raf icin AVM yonetimi **hasar bedelini dogrudan kisisel kotaya** (veya co-op modunda ortak takım bütçesine) ekler. Her yeri kirmak finansal intihara donusur.

### Loophole 2: Ortak Kota Bele sci (Freeloader)
**Acik:** Bir oyuncu para biriktirir ama ortak kotaya katki yapmaz, herkes kaybeder.
**Cozum:** **Vergi Kacakcisi** mekanigi: ortak kota dolmazsa en cok parasi olup odeme yapmayan oyuncu tespit edilir, guvenlik onu nezarethaneye atar, eksik kalan kota miktarı ile birlikte %10 ceza ücreti (vergi cezası) oyuncunun bakiyesinden zorla ortak kotaya çekilir.

### Loophole 3: Kasa Onu QTE Spam (Stunlock)
**Acik:** 4 oyuncu ayni anda birbirine QTE basarsa sonsuz animasyon dongusu olusur.
**Cozum:** Sira atlama denemesinden sonra **10 saniye cooldown** — basarili veya basarisiz, oyuncu 10 sn tekrar deneyemez.

### Loophole 4: Ucuz Mal Kalkani
**Acik:** Oyuncu sepetin ustune degersiz mallari dizer, pahali mallari korur.
**Cozum:** Dokulen/firlatilan mallar **rastgele** secilir, "en son eklenen" degil.
