# `.ipu` video

Two videos on this disc, both the intro logo sting. **There are no `.pss` files**
- the earlier note in the README claiming MPEG-2 program streams was wrong, and
this disc carries raw IPU only.

```bash
obz video info      # header and index of every video
obz video split     # -> extracted/ipu/<name>/<name>_0000.ipu, one per frame
```

| Video | Size | Frames | Index entries | Bytes |
|---|---|---|---|---|
| Logo01 | 320x240 | 121 | 121 | 995,177 |
| Logo02 | 320x240 | 120 | 120 | 985,549 |

## Container

A 16-byte header, then the frames end to end:

```
char[4]  "ipum"
u32      payload size
u16      width          320
u16      height         240
u32      frame count
```

The declared payload size trails the real file length by about a kilobyte on
both, so there is a little padding after the last frame.

## The index is not optional

**The frames are not self-delimiting.** Nothing in the bitstream marks where one
ends, which is what the matching `.ipx` is for: one `u32` offset per frame from
the start of the file. Its entry count equals the header's frame count exactly
on both videos, and the first entry is 16 - immediately after the header.

This matters in practice. Handing the whole file to FFmpeg, which has decoded
IPU since 4.4, fails on 118 of 121 frames: its demuxer expects to find frame
boundaries itself and there are none to find. Split on the index first, wrap
each frame in the same header with a frame count of one, and every frame decodes
- 121 of 121, no failures.

The first 24 bytes of every frame are byte-identical, which looks alarming until
you see the video: it opens on a near-empty background, so the leading
macroblocks really are the same.

## The codec

MPEG-2-class intra coding, and not decoded here. FFmpeg does it:

```bash
obz video split
ffmpeg -i extracted/ipu/Logo01/Logo01_0000.ipu frame.png
ffmpeg -framerate 25 -i frames/Logo01_%04d.png -c:v libx264 -pix_fmt yuv420p Logo01.mp4
```

25 fps is what the container implies: FFmpeg reports 4.84 s for 121 frames.

Splitting stays in `obz` because it is container work, which is this project's
business; the codec stays in FFmpeg, which already has a tested decoder.
