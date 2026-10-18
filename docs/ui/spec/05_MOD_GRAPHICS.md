# 05 · Mod Graphics and Assets

The game uses an FM-style mod model: users add roster/database files, player photos and team logos to user folders, and files map to entity IDs. This file covers how the UI finds, decodes, caches and presents those assets, and how modders are supported.

**Do not change the mod on-disk format, mapping files or entity IDs** (00 §5 rule 1). Read the existing loader first and record its format in `AUDIT.md`. Everything below sits on top of it.

---

## 1. Principles

1. **Complete with zero mods.** Every image slot has an intentional vector fallback; nothing is ever an empty box or a broken-image glyph.
2. **Better with mods.** Real photos and logos drop into the same frames and look designed-for, whatever their size or aspect.
3. **Never crash, never block.** Bad files are skipped with one log line; decoding never happens on the UI thread.
4. **Transparent to modders.** They can see what loaded, what failed and why, find any entity's ID, and reload without restarting.

## 2. Asset kinds and slots

| Kind | Slots (DIPs) | Notes |
|---|---|---|
| Player photo | XS 24, S 32, M 48, L 96, XL 160, Hero 320–420 tall | Cutouts or photos, any common aspect (01 §11) |
| Team logo | 16, 20, 24, 32, 48, 64, 128, 256 | Use a pack's small "icon" variant for ≤ 32 if the format provides one |
| League / competition logo | 24, 48, 128 | |
| Staff / coach photo | as player | |
| Arena or team banner | wide hero (≈ 3:1) | Only if the format supports it |
| Uniforms, courts, other kinds | — | Only if the existing format supports them; give each kind slots the same way |

## 3. The asset resolver

One service resolves every image in the UI. Views and view models never open image files themselves (02 §11).

```csharp
public interface IAssetResolver
{
    IImage Fallback(AssetKey key, AssetSlot slot);              // instant, vector, never null
    bool TryGetCached(AssetKey key, AssetSlot slot, out IImage image);
    Task<IImage> ResolveAsync(AssetKey key, AssetSlot slot, CancellationToken ct);
    AssetStats Stats { get; }                                    // packs, mapped, failed, cache entries/bytes/hit rate
    IReadOnlyList<AssetIssue> Issues { get; }                    // failures with file, reason, pack
    Task ReloadAsync();                                          // rescan mods, clear cache, raise Reloaded
    event EventHandler Reloaded;
}
// AssetKey = (EntityKind, EntityId); AssetSlot = (Kind, SizeDips)
```

**Resolution order:** enabled mods in the existing load order → base-game assets → generated fallback.

**Binding pattern** (in `PlayerHeadshot`, `TeamLogo`, and similar):
1. Show the cached image if present, else the fallback, immediately.
2. Start `ResolveAsync` with a cancellation token tied to the control's current key.
3. When the control is recycled by virtualization (its key changes), cancel the old request, and ignore any result whose key no longer matches. A recycled row must never flash another player's face.
4. Swap in the resolved image with a 120ms fade (none under reduced motion).

## 4. Decoding and caching

- Decode at the drawn size: slot DIPs × render scaling × UI scale, rounded **up** to a bucket: 32, 64, 128, 256, 512, 1024 px. Buckets raise cache hit rates across slots.
- Decode on a background thread with `Bitmap.DecodeToWidth` / `DecodeToHeight`; cap any source at 4096 px on its long edge before further work.
- **Cache:** LRU keyed by (file path, bucket), sized by estimated bytes (w × h × 4). Default budget 256 MB, configurable. Prefetch the visible range plus one screen ahead in long lists.
- Count hits and misses for the Mods & Graphics screen.
- Disposal per 02 §11 — never dispose an image that might still be displayed.

## 5. Robustness matrix

Each case produces the fallback, one log line per file per session, and an entry in `Issues`. None may throw into the UI.

| Case | Expected behavior |
|---|---|
| File missing (mapping points nowhere) | Fallback; issue "missing file" |
| Zero-byte file | Fallback; issue "empty file" |
| Corrupt or truncated image | Fallback; issue "decode failed" |
| Wrong extension (JPEG named `.png`) | Decode by content if the decoder can; otherwise fallback + issue |
| Huge image (e.g. 8000×8000) | Downscaled decode at the bucket size; issue "oversized" (warning only) |
| Extreme aspect (> 3:1 for a logo, > 2:1 for a headshot) | Logo letterboxed; headshot uses fallback; issue "unsupported aspect" |
| Animated image | First frame or fallback; issue |
| Unreadable (permissions, locked) | Fallback; issue |
| Path traversal (`..`), absolute path or symlink escaping the mod root in a mapping | **Rejected** before any file access; issue "unsafe path" |
| Duplicate ID across packs | Load order decides the winner; issue "conflict" listing both packs |
| 5,000+ images in one pack | Scan off the UI thread with progress; UI stays responsive; cache stays within budget |

## 6. Fallback designs

All vector, all token-driven, all themed by the entity's team:
- **Player silhouette:** neutral head-and-shoulders path on the team gradient; jersey number at L and above; initials optional.
- **Team monogram plate:** abbreviation in the display font on `TeamPrimaryRaw`, a `TeamSecondaryRaw` diagonal stripe, `OnPrimaryRaw` text.
- **League badge:** short name in display caps on `Surface2` with the accent stripe.

Fallbacks are designed to look like part of the game's art direction — screenshots with zero mods must pass visual QA on their own.

## 7. Mods & Graphics screen (S30)

T4 list–detail.

- **Packs list:** name or folder, kind (faces, logos, roster/database, skin), items mapped, items failed, conflicts, enabled state (read-only if the loader can't toggle), load-order position.
- **Detail:** pack path, counts by kind, issues table (file, reason, suggested fix), sample thumbnails.
- **Summary bar:** total packs, faces, logos, issues; cache entries, MB used, hit rate.
- **Actions:** Reload graphics (no restart; the whole UI re-resolves), Open mods folder, Open logs folder, Copy report (plain text for bug reports).
- **Show entity IDs** toggle (also in Settings): adds IDs, with copy buttons, to profiles, hover cards and table context menus — this is how modders learn which file name maps to whom.
- Empty state for zero packs explains where packs go and links to the modding guide.

## 8. Roster and database mods in the UI

- New career (S03) lists the roster sets the loader exposes (name, author, version, counts, compatibility) and lets the user pick only in ways the loader supports.
- If a roster mod references images by ID, they resolve through the same pipeline; no special cases.

## 9. Skin token overrides (P2)

A pack may ship `skin.json`:

```json
{ "name": "Retro Arena", "author": "…", "version": "1.0",
  "tokens": { "Canvas": "#101820", "Surface1": "#18222E", "AccentFallback": "#FF5A1F" } }
```

- Only known token names; values validated; the contrast rules in 06 §3 are re-checked on load and any token that would break them is ignored with an issue.
- No layout or template overrides — tokens only.
- If deferred, log it in `FOLLOWUPS.md` as P2.

## 10. Modder documentation — `docs/modding/GRAPHICS.md`

Write it from the actual loader code, for people who are not programmers:

1. Where packs go on Windows, macOS and Linux.
2. Pack folder structure and the mapping-file format as implemented, with a complete small example.
3. Naming by entity ID, and how to find IDs in game (Show entity IDs).
4. Recommended image specs (adjust to anything the existing format already mandates):

| Asset | Recommended | Accepted | Tips |
|---|---|---|---|
| Player photo | 512×512 PNG, transparent cutout, head and shoulders, bottom-aligned | 256–1024 px; 1:1 or about 1.37:1; PNG or JPEG | Transparent cutouts look best on team gradients |
| Team logo | 512×512 PNG, transparent | 128–1024 px, roughly square | Optional 64×64 icon variant for tiny slots |
| League logo | 512×512 PNG, transparent | | |
| Staff photo | as player photo | | |

5. Testing a pack: Reload graphics, the Mods & Graphics screen, reading the issues list and logs.
6. Troubleshooting table: every case in §5 with the message the user sees and the fix.
7. A rights reminder: pack authors are responsible for having permission to distribute the images they include.
