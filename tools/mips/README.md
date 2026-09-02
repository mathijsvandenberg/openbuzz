# MIPS scanner

`SCES_533.05` has no symbols, so the way into it is the string pool. A name like
`ANIMATEDMODEL_HOSTESS` sits in `.rodata` at a known address, and any code that
uses it has to build that address with a `lui`/`addiu` (or `lui`/`ori`) pair.
`scan.py` sweeps `.text` for those pairs, reports the sites, and disassembles
around them.

    python tools/mips/scan.py 0042DCD0 004209F0

It also has `callers(addr)`, which finds every `jal` to a given routine - that is
what identified the model-placement function from its eighteen call sites.

The disassembler covers the integer and load/store subset plus enough COP1 to
recognise float work. Anything else prints as `.word`, which is deliberate:
an instruction this does not know shows up as raw rather than as a guess.

The ELF loads at one `PT_LOAD`: file offset `0x1000` to vaddr `0x00100000`.
Sections worth knowing are `.text` `0x00100000`, `.data` `0x00390B80`,
`.rodata` `0x0041A800` and `.lit4` `0x0043F980`, the last being float literals.

Requires the disc; the executable is not in this repository.
