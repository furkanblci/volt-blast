# Play Store listing

Paste these into Play Console. Character counts are
against the limits the console enforces.

## Kısa açıklama — Türkçe (71/80)

```
Neon bloklarla satır patlat. Reklamsız, internetsiz, sakin bir bulmaca.
```

## Tam açıklama — Türkçe (1050/4000)

```
Volt Blast, karanlık bir ekranda parlayan neon bloklarla oynanan 8x8'lik bir blok bulmacası.

Süre yok. Hamle sınırı yok. Tepside bekleyen üç parçadan hiçbiri tahtaya sığmadığında oyun biter — yani her yerleştirme, sonraki eline dair bir bahis.

NASIL OYNANIR
• Tepsideki parçaları tahtaya sürükle
• Bir satırı veya sütunu doldur, patlasın
• Arka arkaya temizle, combo büyüsün
• Üç parça da sığmaz olduğunda koşu biter

OYUNUN SANA SÖYLEDİKLERİ
• Artık hiçbir yere sığmayan parçalar söner — ölmekte olan bir tahta bunu sana söyler
• Tahta dolmaya başlayınca çerçeve ısınır ve nabız gibi atar
• Temizlediğin satır sayısına ve combo'na göre tüm kenar alev alır
• Kısa, şekilli titreşimler — yerleştirme hafif bir tık, seri çift vuruş

DÜRÜST OLARAK
• Reklam yok
• Uygulama içi satın alma yok
• İnternet gerekmez, tamamen çevrimdışı
• Hesap yok, kayıt yok, izin istemiyor
• Rekorun cihazında kalır

Tek mod: klasik. Enerji sistemi, günlük görev, bekleme süresi yok. Açarsın, oynarsın, kapatırsın.

Titreşim ve parlama efektleri ayarlardan kapatılabilir.
```

## Short description — English (64/80)

```
Drop neon blocks, blast lines. No ads, no timers, plays offline.
```

## Full description — English (1048/4000)

```
Volt Blast is an 8x8 block puzzle played with neon tiles on a dark board.

No timer. No move limit. The run ends when none of the three pieces on offer fit anywhere — so every placement is a bet on what you will be handed next.

HOW IT PLAYS
• Drag pieces from the tray onto the board
• Fill a row or a column and it clears
• Clear on consecutive turns and the combo climbs
• When nothing fits any more, the run is over

WHAT THE GAME TELLS YOU
• Pieces that no longer fit anywhere dim, so a dying board says so
• The frame warms and pulses as the board fills
• The whole edge lights up in proportion to the lines and the combo you just pulled off
• Short, shaped vibrations — a light tick on a placement, a double knock on a streak

PLAINLY
• No ads
• No in-app purchases
• No internet needed, fully offline
• No account, no sign-up, no permissions requested
• Your best score stays on your device

One mode: classic. No energy system, no daily quests, no waiting. Open it, play, close it.

Vibration and glow can both be switched off in settings.
```

## Graphics in this folder

| File | Slot | Size |
|---|---|---|
| `icon_512.png` | Uygulama simgesi | 512x512, opaque, no alpha |
| `feature_graphic.png` | Özellik grafiği | 1024x500 |
| `screenshot_1.png` | Telefon ekran görüntüsü | 1080x1920 — a played board |
| `screenshot_2.png` | Telefon ekran görüntüsü | 1080x1920 — a clear, mid-combo |
| `screenshot_3.png` | Telefon ekran görüntüsü | 1080x1920 — the board filling up |
| `screenshot_4.png` | Telefon ekran görüntüsü | 1080x1920 — the best-score screen |

Play wants at least two phone screenshots; four are here.

## Claims to keep honest

Everything in the descriptions is checkable in the build: no ads, no in-app purchases, no
network use, no account, no runtime permission prompts.

Nothing here claims a player count, a rating, or an award.

## Before you publish

Two things in the project are development-only and should go first:

1. **`com.unity.modules.unityanalytics`** in `Packages/manifest.json` is what puts
   `android.permission.INTERNET` in the manifest — read out of the built APK, it is there
   alongside VIBRATE. Nothing in the game uses the network, so the permission is only a
   question you will have to answer on the data-safety form. Removing the module drops it.
2. **`com.coplaydev.unity-mcp`** is the editor bridge used to build this. It is an Editor
   package and should not affect a player build, but it has no business in a release.

Also still outstanding: the app is signed with the debug keystore, which installs on a
device but cannot be uploaded. Publishing needs a release keystore set in Player Settings →
Publishing Settings, and `bundleVersionCode` raised for every upload.
