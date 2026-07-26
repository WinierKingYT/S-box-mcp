# s&box MCP AI Bridge

s&box Editor sahnesini Codex, Claude Desktop, Cursor ve Windsurf üzerinden
incelemek ve düzenlemek için yerel bir Model Context Protocol sunucusudur.
Sunucu yalnız `localhost` üzerinde çalışır ve bütün MCP isteklerinde
`x-api-key` doğrulaması yapar.

[English documentation](README.en.md)

## Özellikler

- Streamable HTTP (`/mcp`) ve geriye uyumlu SSE (`/sse`, `/messages`)
- Sahne ağacını, seçimi, nesneleri ve component özelliklerini sorgulama
- Nesne oluşturma, silme ve transform/component özelliklerini güncelleme
- AI değişikliklerini s&box Ctrl+Z/Ctrl+Y geçmişine kaydetme
- Yalnız MCP değişikliklerini çalıştıran `undo_scene_change` ve
  `redo_scene_change` araçları
- Viewport ekran görüntüsü, kamera odaklama ve isteğe bağlı konsol komutları
- API anahtarı, hız sınırı, CORS kontrolü ve güvenli ekran görüntüsü yolları

## Hızlı kurulum

1. Sürüm ZIP'ini ayrı bir klasöre çıkarın.
2. PowerShell'de bu klasörü açın.
3. Kurucuyu çalıştırın:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```

4. s&box'ı ve yapılandırılan AI istemcilerini tamamen kapatıp yeniden açın.
5. s&box Editor içinde bir sahne açın.
6. Sağlık kontrolünü çalıştırın:

   ```powershell
   Invoke-WebRequest http://localhost:29017/health
   ```

Kurucu varsayılan Steam konumunu bulamazsa:

```powershell
.\install.ps1 -SboxRoot "D:\SteamLibrary\steamapps\common\sbox"
```

Belirli istemcileri yapılandırmak için:

```powershell
.\install.ps1 -Clients Codex,Cursor
```

Kurucu mevcut istemci ayarlarını değiştirmeden önce zaman damgalı yedek alır.
Yeni bir `SBOX_MCP_API_KEY` üretir ve anahtarı yapılandırma dosyalarına
yazmak yerine kullanıcı ortam değişkeninde saklar.

## İstemci yapılandırmaları

Hazır örnekler [`clients`](clients) klasöründedir.

| İstemci | Kullanılan ayar |
| --- | --- |
| Codex | `%USERPROFILE%\.codex\config.toml` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |
| Windsurf | `%USERPROFILE%\.codeium\windsurf\mcp_config.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |

Codex, Cursor ve Windsurf `http://localhost:29017/mcp` adresine doğrudan
bağlanır. Claude Desktop, ZIP içindeki self-contained
`SboxMcp.StdioBridge.exe` üzerinden bağlanır. Köprü yalnız loopback HTTP
adreslerini kabul eder.

Ortam değişkenini elle tanımlamak gerekirse:

```powershell
[Environment]::SetEnvironmentVariable(
  "SBOX_MCP_API_KEY",
  "uzun-rastgele-bir-deger",
  "User"
)
```

Değişiklikten sonra GUI uygulamalarını yeniden başlatın.

## Yapılandırma

Sunucu ayarları şu sırayla birleştirilir; sonraki katman öncekini ezer:

1. Kod içindeki güvenli varsayılanlar
2. `Editor/mcp_config.json`
3. Proje kökündeki `mcp_config.json`
4. `%APPDATA%\sbox-mcp\mcp_config.json`
5. `SBOX_MCP_API_KEY` ve `SBOX_MCP_PORT`

Önemli ayarlar:

- `Port`: Varsayılan `29017`
- `MaxRequestsPerSecond`: Varsayılan `20`
- `RequestTimeoutMs`: Varsayılan `30000`
- `CorsOrigins`: Tarayıcı origin allowlist'i; native istemciler origin göndermez
- `EnableConsoleCommands`: Güvenlik nedeniyle varsayılan `false`
- `ScreenshotDirectory` ve `ScreenshotDirectoryBase`: Görüntü çıktı alanı
- `ConfigVersion`: Geçerli şema sürümü `2`
- `ApprovalMode`: Varsayılan `AllMutations`; alternatifler `HighRiskOnly` ve `Disabled`
- `ApprovalTimeoutSeconds`, `MaxPendingApprovals`, `MaxBatchOperations`
- `PersistHistory`, `HistoryLimit`, `HistoryRetentionDays`

Bozuk bir katman uyarıyla atlanır. Geçersiz port, limit veya timeout değerleri
güvenli varsayılanlara döner. Sürümü olmayan v1 dosyaları yüklenirken v2'ye
taşınır, sürümlü yedeği alınır ve atomik güncellenir. Daha yeni ve
desteklenmeyen bir config sürümü sunucunun başlamasını engeller.

## MCP araçları

Sahne sorguları:

- `get_scene_hierarchy`, `list_objects`, `get_selection`
- `get_object_properties`

Sahne değişiklikleri:

- `create_object`, `delete_object`, `update_object_properties`
- `duplicate_object` — nesneyi çocukları ve componentleriyle çoğaltır
- `set_parent` — varsayılan olarak dünya transformunu koruyarak parent değiştirir
- `add_component`, `remove_component`
- `instantiate_prefab`, `break_prefab`
- `undo_scene_change`, `redo_scene_change`
- `submit_scene_changes`, `get_change_request`, `cancel_change_request`
- `get_server_status`

Editör yardımcıları:

- `focus_camera_on_object`, `capture_viewport_screenshot`
- `execute_console_command` — yalnız açıkça etkinleştirilirse
- `list_tools`

Varsayılan olarak sahne mutasyonları hemen uygulanmaz; `pending_approval`
durumu ve bir `requestId` döndürür. Editörde **Editor → MCP Server → Control
Center** panelinden önizlemeyi onaylayın veya reddedin. AI,
`get_change_request` ile sonucu takip edebilir. `submit_scene_changes`, en fazla
50 mutasyonu tek onay ve tek `[MCP]` undo girdisi altında toplar.

Her başarılı sahne değişikliği tek `[MCP] ...` undo girdisi oluşturur. AI undo
aracı, geçmişin tepesinde kullanıcıya ait manuel bir işlem varsa onu geri
almayı reddeder. Editörde Ctrl+Z/Ctrl+Y normal şekilde çalışır. Prefab yolları
proje içindeki `/` ayrımlı `.prefab` asset yolları olmalıdır.

## Örnek istekler

İstemciye doğal dille şunları söyleyebilirsiniz:

- “Sahnedeki nesneleri listele ve ışıkları özetle.”
- “SpawnPoint adında, 0 0 64 konumunda bir nesne oluştur.”
- “Bu nesnenin konumunu 128 0 64 yap.”
- “Nesneyi çocuklarıyla çoğalt ve kopyayı Environment altına taşı.”
- “Nesneye BoxCollider componenti ekle.”
- “prefabs/props/crate.prefab dosyasını sahneye ekle.”
- “Son yaptığın sahne değişikliğini geri al.”
- “Viewport görüntüsü al ve kaydedilen yolu bildir.”

Ham bağlantı kontrolü:

```powershell
$headers = @{
  "x-api-key" = $env:SBOX_MCP_API_KEY
  "Content-Type" = "application/json"
}
$body = @{
  jsonrpc = "2.0"
  id = 1
  method = "initialize"
  params = @{ protocolVersion = "2025-11-25"; capabilities = @{} }
} | ConvertTo-Json -Depth 5
Invoke-RestMethod http://localhost:29017/mcp `
  -Method Post -Headers $headers -Body $body
```

## Güvenlik

- Sunucuyu `localhost` dışına açmayın.
- API anahtarını repoya, ekran görüntüsüne veya hata raporuna koymayın.
- `CorsOrigins` için `*` kullanmayın.
- Konsol komutlarını yalnız güvenilir yerel kullanımda açın.
- İstemci araç çağrılarındaki yazma onaylarını açık tutun.
- Dağıtım ZIP'inin yanındaki `.sha256` dosyasıyla paketi doğrulayın.

## Sorun giderme

**401 Unauthorized:** `SBOX_MCP_API_KEY` değişkeninin hem s&box hem istemci
sürecinde görüldüğünü kontrol edin ve ikisini yeniden başlatın.

**Araçlar görünmüyor:** Önce `/health` adresini kontrol edin; ardından istemci
ayarındaki URL, anahtar başlığı ve JSON/TOML sözdizimini doğrulayın.

**Claude bağlantısı kapanıyor:** Yapılandırmadaki köprü yolunun mutlak olduğunu
ve `SboxMcp.StdioBridge.exe` dosyasının mevcut olduğunu kontrol edin. Claude'u
sistem tepsisinden tamamen kapatıp açın.

**Port kullanımda:** `SBOX_MCP_PORT` değerini ve istemci URL'lerini aynı yeni
porta taşıyın.

**Access denied / URL reservation:** Yönetici PowerShell'de yalnız seçtiğiniz
loopback portu için şu komutu kullanın:

```powershell
netsh http add urlacl url=http://localhost:29017/ user="$env:USERDOMAIN\$env:USERNAME"
```

**Ekran görüntüsü kaydedilemiyor:** `ScreenshotDirectoryBase` ve dizin yazma
izinlerini kontrol edin. Dosya adlarında dizin veya `..` kullanılamaz.

## Kaynaktan geliştirme

Gereksinimler: Windows, s&box Editor ve .NET 10 SDK.

```powershell
dotnet test .\mcp_server.slnx -p:StaticWebAssetsEnabled=false
.\scripts\build-release.ps1 -Version 1.2.0
```

Release çıktısı `dist` klasöründe ZIP ve SHA-256 olarak üretilir.

### GitHub otomasyonu

- `CI`: Windows GitHub-hosted runner üzerinde depo doğrulaması, köprü testleri
  ve self-contained publish kontrolü yapar.
- `s&box Full Validation`: `self-hosted`, `Windows`, `sbox` etiketli ve s&box
  kurulu runner üzerinde bütün editör testlerini elle çalıştırır.
- `Release`: `v1.2.0` biçimindeki tag'lerde ZIP, SHA-256 ve GitHub Release
  üretir.
- Dependabot, NuGet ve GitHub Actions bağımlılıklarını aylık kontrol eder.

Varsayılan dışındaki s&box kurulumu için MSBuild özelliği kullanılabilir:

```powershell
dotnet test .\mcp_server.slnx `
  -p:StaticWebAssetsEnabled=false `
  -p:SboxRoot="D:\SteamLibrary\steamapps\common\sbox"
```

## Kaldırma

ZIP klasöründen:

```powershell
.\uninstall.ps1
```

API anahtarını da kullanıcı ortamından kaldırmak için:

```powershell
.\uninstall.ps1 -RemoveApiKey
```
