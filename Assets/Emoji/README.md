# Noto Color Emoji

Images extracted from `2D/fonts/NotoColorEmoji.ttf` of [googlefonts/noto-emoji](https://github.com/googlefonts/noto-emoji)
at commit `e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e` (Emoji 17.0), scaled to 48 px high.

**License:** SIL Open Font License 1.1, see `THIRD_PARTY_LICENSES/OFL-1.1.txt`.

## Archive layout

- `<sequence>.png` - lowercase hex code points joined by `_`, without U+FE0F, e.g. `1f469_200d_2764_200d_1f468.png`.
- `aliases.txt` - `<sequence> <sequence>` pairs for sequences that share an image.
- `text-default.txt` - single code points that Unicode shows as text unless followed by U+FE0F.

## Rebuild

Download the font and [emoji-data.txt](https://www.unicode.org/Public/17.0.0/ucd/emoji/emoji-data.txt) for the
matching Unicode version, then run from the repository root:

```powershell
dotnet run --project tests/EmojiPackTool/EmojiPackTool.csproj -c Release -- NotoColorEmoji.ttf emoji-data.txt Assets/Emoji/NotoColorEmoji.zip 48
```
