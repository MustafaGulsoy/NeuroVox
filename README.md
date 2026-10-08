# NeuroVox

Konuşma tabanlı bilişsel çalışma araştırma platformu: ses kaydı → otomatik (aday) ölçümler → kör terapist anotasyonu → klinik sonuçla ilişki / model eğitimi.

> **Araştırma yazılımıdır, tanı aracı değildir.** AI çıktıları "aday" ölçümdür; klinik etiket yalnızca terapist katmanı ve bağımsız belgelenmiş klinik sonuçtur.

## Mimari

| Parça | Konum | Görev |
|---|---|---|
| API (.NET 8) | `NeuroVox.WebApi` + katmanlar | REST API, kör anotasyon, istatistik, export, etiketleme arayüzü (`/labeling.html`) |
| Arayüz (Angular 20) | `frontend` | Giriş, katılımcı/ziyaret/kayıt yönetimi, kör anotasyon, analiz, kullanıcı ve roller. nginx ile servis edilir; `/api` aynı origin'den API'ye gider |
| Kimlik | `BaseAuth.Library` NuGet paketi | Giriş, JWT, kullanıcı ve rol uçları API'nin içindedir (`/api/Auth`, `/api/Users`, `/api/Roles`); ayrı bir auth servisi yoktur |
| AI servisi (FastAPI) | `services/neurovox_ai` | Whisper transkript, VAD, Türkçe morfosentaks, aday ölçümler, `/predict` |
| Eğitim hattı | `services/neurovox_pipeline` | `train.py` (katılımcı bazlı bölme, sızıntısız), `irr.py` |

Akış: `POST /api/SpeechRecordings/upload` → `POST /{id}/analyze` (202, arka plan kuyruğu) → `GET /{id}/analysis` (durum + ölçümler).

## Çalıştırma (Docker)


```bash
cp .env.example .env        # tüm değerleri doldur (JWT anahtarı >= 32 karakter)
docker compose up -d --build
```

- API `127.0.0.1:8080`, arayüz `127.0.0.1:8081` üzerinde yayınlanır (`CUSTOMER_ID` = kurum GUID'i); **önlerine TLS sonlandıran bir reverse proxy (nginx/Caddy) koyun**. AI servisi dışarı açılmaz.
- API açılışta EF migration'larını uygular (`NeuroVox:AutoMigrate=false` ile kapatılır).
- **İlk kurulum:** BaseAuth şeması (`dotnet ef migrations script --idempotent` ile BaseAuth.Persistence'tan üretilir), ilk müşteri, admin kullanıcı ve endpoint izinleri SQL ile tohumlanır; bu repo bunu otomatik yapmaz. Yeni `[AuthorizeDefinition]` eklenince ilgili `auth."Endpoints"` satırı ve rol bağı da eklenmelidir.
- Sağlık: `GET /health` (API), `GET /health` (AI, iç ağ).

## Yetkilendirme (BaseAuth rolleri)

API, BaseAuth'un `RolePermissionFilter`'ını kullanır: `[AuthorizeDefinition]` taşımayan her aksiyon kimliği doğrulanmış kullanıcıya bile **reddedilir**, taşıyanlar için rolün ilgili endpoint iznine sahip olması gerekir. İlk kurulumda (veya yeni sürümde) BaseAuth'un *ApplicationServices / AuthorizationEndpoints* ekranlarından NeuroVox endpoint'lerini tarayıp rollere atayın. Bu sürümde eklenen izinler: `Delete Participants`, `Delete SpeechRecordings`, `Get Export`, `Get/Post ResearchProtocols`, `Get/Post Stimuli`.

## NuGet / Nexus

`BaseAuth.Library` (2.7.0) özel Nexus'tan gelir: `nuget.config` → `http://69.62.120.208:1010/repository/nuget-hosted` (anonim okuma, `BaseAuth.*` yalnızca bu kaynaktan — dependency confusion koruması). Nexus VPS'te `nexus` adlı Docker konteyneridir (veri: `nexus-data` volume). Kapalıysa:

```bash
ssh root@69.62.120.208 'docker start nexus'      # konteyner yoksa:
ssh root@69.62.120.208 'docker run -d --name nexus --restart unless-stopped -p 1010:8081 -v nexus-data:/nexus-data -e INSTALL4J_ADD_VM_PARAMS="-Xms512m -Xmx1200m -XX:MaxDirectMemorySize=1g" sonatype/nexus3:3.92.2'
```

Açılış ~1-2 dk sürer (`curl http://69.62.120.208:1010/service/rest/v1/status`). Sunucudaki `cpu-guard.sh` CPU ≥ %90 olunca izin listesinde olmayan konteynerleri durdurur; `nexus` listededir. Bağlantı şimdilik düz HTTP; paket yayımlamak için kimlik bilgisi gerekir, TLS (nginx-proxy arkası) önerilir.

## Arayüz geliştirme

```bash
cd frontend && npm ci
# public/config.json içine customerId yazın; API için proxy ile veya apiBase ile yönlendirin
npx ng serve
```

Açıklamalar: oturum `sessionStorage`'da tutulur (localStorage değil); 401 gelirse bir kez token yenilenir, yine 401 ise "yetkiniz yok" gösterilir (BaseAuth izin eksikliğini de 401 ile bildirir). Kayıt sesi bearer token gerektirdiği için blob olarak indirilip çalınır. CSP satır içi script'e izin vermez.

## Geliştirme

```bash
cp NeuroVox.WebApi/appsettings.Example.json NeuroVox.WebApi/appsettings.json   # gitignored
dotnet test
cd services/neurovox_ai && pip install -r requirements-dev.txt && pytest
```

Yeni migration: `cd NeuroVox.WebApi && dotnet ef migrations add <Ad> -p ../NeuroVox.Persistence -s . --context NeuroVoxDbContext`

## Model eğitimi

1. Katılımcı, ziyaret, kayıt, klinik sonuç gir; kayıtları analiz et.
2. `GET /api/Export/training-set.csv` (Bearer + `customerid` header) → `data.csv`
3. `python services/neurovox_pipeline/train.py --features data.csv --out artifacts`
4. `artifacts/` klasörü compose'ta AI servisine `/models` olarak bağlanır; servisi yeniden başlat. Model yokken `/api/Predictions/predict` 503 döner.

Export yalnızca aktif onamı olan katılımcıları ve `ConversionToAD` / `StableMCI` sonuçlarını içerir.

## Güvenlik modeli

- **Tenant izolasyonu:** JWT'deki müşteri kimliği esastır; `customerid` header'ı token ile uyuşmazsa 403. Tüm müşteriye ait tablolarda EF global query filter vardır.
- **Kör anotasyon:** Rater kimliği token'dan gelir; bir rater yalnızca kendi anotasyonlarını görür.
- **Ses dosyaları:** uzantı + magic-byte doğrulaması, 200 MB sınırı, istemciden yol kabul edilmez (DB'de yalnızca dosya adı). AI servisi yalnızca `/data/audio` altını okur ve API anahtarı ister.
- **İç ağ:** `ai` ve `postgres` yalnızca compose iç ağındadır.

## KVKK / etik

- Onam kaydı zorunlu: onamı olmayan (veya geri çekmiş) katılımcı için kayıt yüklenemez (`409`). `POST /api/Participants/{id}/consent`.
- Silme hakkı: `DELETE /api/Participants/{id}` ses dosyalarını diskten siler, transkript/not/kişisel alanları temizler, ilişkili kayıtları soft-delete yapar. `DELETE /api/SpeechRecordings/{id}` tek kaydı siler.
- **Uygulama düzeyinde yapılmayanlar (operasyon sorumluluğu):** disk/volume şifrelemesi (`audio` ve `pgdata` volume'leri), veritabanı yedekleri ve saklama süresi politikası, aydınlatma metni, veri işleme sözleşmeleri, erişim loglarının periyodik gözden geçirilmesi.
- `participantCode` takma addır; gerçek kimlikle eşleştirme tablosunu bu sistemde tutmayın.

## Bilinen sınırlar

- Analiz kuyruğu süreç içidir (restart'ta DB'den yeniden kuyruğa alınır). Birden çok API kopyası çalıştırılacaksa harici kuyruğa taşıyın.
- Whisper CPU'da çalışır; uzun kayıtlar dakikalar sürebilir (tek iş aynı anda).
- Türkçe morfosentaks ölçümleri `zeyrek` yoksa sezgisel yedekle çalışır ve `nlp_backend` ölçümünde işaretlenir; hepsi `EXPERIMENTAL`.
- Etiketleme arayüzü sade bir statik sayfadır; ses, token gerektirdiği için blob olarak indirilir.


## Study plan coverage (speech + ACE-III; no MRI, no video)

| Plan item | Where |
|---|---|
| Inclusion/exclusion criteria | Participant detail > Uygunluk (`PUT /api/Participants/{id}/eligibility`); optional; set `NeuroVox:RequireEligibility=true` to refuse uploads unless eligible |
| Ethics approval | Research > protocol > Etik kurul onayı (`PUT /api/ResearchProtocols/{id}/ethics`); optional; set `NeuroVox:RequireEthicsApproval=true` to refuse uploads without it |
| Speech rate, TTR/MATTR, pauses, POS ratios | AI service `/analyze` |
| Language-error candidates (unanalysable words, verbless sentences, fillers, self-corrections) | `turkish_nlp.error_candidates`; semantic/grammar judgement stays with blind human raters |
| Group tests (Shapiro-Wilk, Welch t / Mann-Whitney U, BH-adjusted p) | Analysis > Grup karşılaştırması (`GET /api/Analysis/group-comparison`, computed by `services/neurovox_ai/stats.py`) |
| Rater agreement + AI vs human | Analysis > Değerlendirici uyumu (`GET /api/Analysis/rater-agreement`) |
| Prediction model (speech vs ACE-III vs combined, grouped CV, AUC CI, sensitivity/specificity) | `services/neurovox_pipeline/train.py`, run from Kaggle > Model eğitimi |
| Per-visit model estimate | Participant detail > Ziyaretler > Model tahmini |

Not in scope by decision: neuroimaging (MTA/GCA/Fazekas) and video; no deep-learning model (classical models are the right size for ~50 participants).

## Multi-tenant administration

- **Institutions** (system admin: Sistem > Kurumlar). Creating one also creates its `KurumAdmin` user and the default `KurumAdmin` / `Doktor` roles. Deactivating an institution blocks sign-in.
- **Roles are matched by id inside the caller's institution** (`TenantRolePermissionFilter`), not by name; BaseAuth's own user/role handlers are not used because they ignore the tenant. `KurumAdmin` manages its institution's users and edits the permissions of `Doktor` and custom roles (Kullanıcılar > Yetkiler); the grantable set is `TenantRoles.Catalog`.
- **System admin** = role `NeuroVox:SystemAdminRole` inside the institution flagged `IsSystemCompany`. Kaggle accounts are one system-wide pool shared by all institutions (enable/disable, scheduled start, stop; kernels on old code are replaced automatically, `NeuroVox:KaggleAutoUpdate`).
- Trained models are stored per institution (`<models>/<customer>/current`).
- Backups: `deploy/backup.sh` (database, audio, models, `.env`) runs daily from cron on the server.
