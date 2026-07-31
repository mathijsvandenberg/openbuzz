# Audio formats

Two containers, both wrapping Sony's 4-bit PS2 ADPCM.

## `.vag` — 124 files, sound effects

Standard Sony format and completely unambiguous: 48-byte big-endian header with
`VAGp` magic, data size at `0x0C`, sample rate at `0x10`, name at `0x20`, then
raw ADPCM. Always mono.

Declared rates on this disc: 11025 Hz (80 files), 22050 Hz (34), 44100 Hz (10).

## `.vgp` — 4449 files, music and speech

No header at all — no magic, no rate, no channel count. The file is a stream of
**2336-byte sectors**:

```
+---------------------------+----------------+
|  2304 bytes ADPCM audio   |  32 B trailer  |
|  = 144 blocks of 16 bytes |                |
+---------------------------+----------------+
```

The sector model is solid. Every file sampled is an exact multiple of 2336
bytes, and every one of the 160k+ audio blocks checked carries a valid ADPCM
predictor (0–4). `obz audio probe` re-checks this.

### Channel layout

Stereo files use **split halves**: within a sector's 2304-byte payload the first
1152 bytes are the left channel and the second 1152 the right. Filter history
carries across sectors per channel; the trailer is skipped, not decoded.

This was settled by listening, not by analysis. An earlier attempt read the
trailer's leading eight bytes as four int16s of per-channel filter history —
which would have made the layout self-verifying, since a correct decode must
reproduce them. It doesn't: mono, split-halves and block-interleaved all match
on essentially no sectors. Whatever the trailer holds, it is not decoder state,
and it is most likely a seek record.

### Mono vs stereo

The **last uint16 of the trailer** flags it. Bit `0x0100` means mono:

| Marker | Source pack | Content | Channels |
|---|---|---|---|
| `0x002C` | `SONGCLIP.PAK` | music clips (`KS`, `LS`, `LF`, `LI`, `UK`, numeric) | 2 |
| `0x012C` | `NETSPEAK.PAK` | Dutch speech (`C_*` 1768, `F_*` 590) | 1 |

The correlation is exact across the disc — no music clip carries `0x012C` and no
speech clip carries `0x002C`. Decoded lengths corroborate it: speech at mono
lands at 2–4 s per line, which is right, where stereo would halve it.

One outlier exists (`LvInt`, marker `0x0116`) and is not yet understood.

`VgpFile.LayoutFor` reads this and picks the layout; `--layout` overrides.

### Sample rate

Not stored anywhere in the stream, and the round data (`*.rnd`, fixed 16-byte
records of uint16 string IDs) carries no durations either, so it cannot be
derived — only chosen and checked by ear.

**44100 Hz**, confirmed by listening; 32000 is audibly too slow. It is also the
only choice consistent with the rest of the disc, since the headered `.vag`
files use 11025/22050/44100 and nothing on the disc uses 32000 or 48000.

## Tools

```bash
obz audio rates      # sample rates declared by the .vag files
obz audio probe      # validate the .vgp sector model
obz audio decode     # -> WAV, auto-detecting mono/stereo from the trailer
```

`decode` takes `--layout split|mono|interleaved` and `--rate` to override
detection, and `--limit` to cap how many files are converted.
