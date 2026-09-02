"""Find where MIPS code builds a given address, and disassemble around it.

The PS2 executable has no symbols, so the way in is the string pool: a name
like ANIMATEDMODEL_HOSTESS sits in .rodata at a known address, and any code
that uses it has to materialise that address with a lui/addiu or lui/ori pair.
"""
import struct, sys

ELF = 'D:/SCES_533.05'
D = open(ELF, 'rb').read()

SECS = {
    '.text':   (0x00100000, 0x001000, 2674528),
    '.data':   (0x00390B80, 0x291B80,  564040),
    '.rodata': (0x0041A800, 0x31B800,  151732),
    '.lit4':   (0x0043F980, 0x340980,    1616),
    '.sdata':  (0x00440000, 0x341000,    4548),
}
TEXT_VA, TEXT_OFF, TEXT_SIZE = SECS['.text']

def read_va(va, n):
    for a, o, s in SECS.values():
        if a <= va < a + s:
            k = o + (va - a)
            return D[k:k + n]
    return b''

def word(va):
    b = read_va(va, 4)
    return struct.unpack('<I', b)[0] if len(b) == 4 else None

REG = ['zero','at','v0','v1','a0','a1','a2','a3','t0','t1','t2','t3','t4','t5','t6','t7',
       's0','s1','s2','s3','s4','s5','s6','s7','t8','t9','k0','k1','gp','sp','fp','ra']
FREG = ['f%d' % i for i in range(32)]

def s16(x):
    return x - 0x10000 if x & 0x8000 else x

def disasm(w, va):
    op = w >> 26
    rs, rt, rd = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31
    sa, fn, imm = (w >> 6) & 31, w & 63, w & 0xFFFF
    si, tgt = s16(imm), ((va + 4) & 0xF0000000) | ((w & 0x3FFFFFF) << 2)
    br = va + 4 + (si << 2)
    R, F = REG, FREG
    if w == 0: return 'nop'
    if op == 0:
        m = {0x20:'add',0x21:'addu',0x22:'sub',0x23:'subu',0x24:'and',0x25:'or',
             0x26:'xor',0x27:'nor',0x2A:'slt',0x2B:'sltu',0x18:'mult',0x19:'multu'}
        if fn in m: return '%-8s %s, %s, %s' % (m[fn], R[rd], R[rs], R[rt])
        if fn == 0x00: return '%-8s %s, %s, %d' % ('sll', R[rd], R[rt], sa)
        if fn == 0x02: return '%-8s %s, %s, %d' % ('srl', R[rd], R[rt], sa)
        if fn == 0x03: return '%-8s %s, %s, %d' % ('sra', R[rd], R[rt], sa)
        if fn == 0x08: return '%-8s %s' % ('jr', R[rs])
        if fn == 0x09: return '%-8s %s' % ('jalr', R[rs])
        return '.word    0x%08X' % w
    if op == 1:
        return '%-8s %s, 0x%08X' % ({0:'bltz',1:'bgez',16:'bltzal',17:'bgezal'}.get(rt,'b?'), R[rs], br)
    if op == 2: return '%-8s 0x%08X' % ('j', tgt)
    if op == 3: return '%-8s 0x%08X' % ('jal', tgt)
    if op in (4,5): return '%-8s %s, %s, 0x%08X' % ('beq' if op==4 else 'bne', R[rs], R[rt], br)
    if op in (6,7): return '%-8s %s, 0x%08X' % ('blez' if op==6 else 'bgtz', R[rs], br)
    if op == 0x0F: return '%-8s %s, 0x%04X' % ('lui', R[rt], imm)
    m = {0x08:'addi',0x09:'addiu',0x0A:'slti',0x0B:'sltiu',0x0C:'andi',0x0D:'ori',0x0E:'xori'}
    if op in m: return '%-8s %s, %s, %d' % (m[op], R[rt], R[rs], si)
    ld = {0x20:'lb',0x21:'lh',0x23:'lw',0x24:'lbu',0x25:'lhu',0x28:'sb',0x29:'sh',0x2B:'sw'}
    if op in ld: return '%-8s %s, %d(%s)' % (ld[op], R[rt], si, R[rs])
    if op == 0x31: return '%-8s %s, %d(%s)' % ('lwc1', F[rt], si, R[rs])
    if op == 0x39: return '%-8s %s, %d(%s)' % ('swc1', F[rt], si, R[rs])
    if op == 0x11:
        fs, ft, fd = (w >> 11) & 31, (w >> 16) & 31, (w >> 6) & 31
        if rs == 0: return '%-8s %s, %s' % ('mfc1', R[rt], F[fs])
        if rs == 4: return '%-8s %s, %s' % ('mtc1', R[rt], F[fs])
        m2 = {0:'add.s',1:'sub.s',2:'mul.s',3:'div.s',6:'mov.s'}
        if fn in m2: return '%-8s %s, %s, %s' % (m2[fn], F[fd], F[fs], F[ft])
        return 'cop1     0x%08X' % w
    return '.word    0x%08X' % w

def refs_to(targets):
    """Every lui+addiu / lui+ori pair in .text that builds one of these addresses."""
    found = {}
    pend = {}
    for i in range(0, TEXT_SIZE - 3, 4):
        va = TEXT_VA + i
        w = struct.unpack_from('<I', D, TEXT_OFF + i)[0]
        op, rs, rt, imm = w >> 26, (w >> 21) & 31, (w >> 16) & 31, w & 0xFFFF
        if op == 0x0F:
            pend[rt] = (imm << 16, va)
            continue
        if op in (0x09, 0x0D) and rs in pend:
            hi, at = pend[rs]
            addr = (hi + s16(imm)) & 0xFFFFFFFF if op == 0x09 else (hi | imm)
            if addr in targets:
                found.setdefault(addr, []).append(at)
            if rt != rs:
                pend.pop(rs, None)
        elif op in (0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E) or op == 0x0F:
            pend.pop(rt, None)
    return found

def dump(va, before=16, after=40):
    for k in range(-before, after):
        a = va + k * 4
        w = word(a)
        if w is None: continue
        mark = '>>' if k == 0 else '  '
        print('  %s 0x%08X  %08X  %s' % (mark, a, w, disasm(w, a)))

if __name__ == '__main__':
    targets = [int(x, 16) for x in sys.argv[1:]]
    hits = refs_to(set(targets))
    for t in targets:
        print('=== references to 0x%08X: %d' % (t, len(hits.get(t, []))))
        for va in hits.get(t, []):
            print('   0x%08X' % va)

def callers(target_va):
    """Every jal to a given address."""
    out = []
    enc = (3 << 26) | ((target_va >> 2) & 0x3FFFFFF)
    for i in range(0, TEXT_SIZE - 3, 4):
        if struct.unpack_from('<I', D, TEXT_OFF + i)[0] == enc:
            out.append(TEXT_VA + i)
    return out
