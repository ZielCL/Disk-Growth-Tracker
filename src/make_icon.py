import struct
from pathlib import Path


def icon_frame(size):
    pixels = bytearray()
    mask = bytearray()
    stride = ((size + 31) // 32) * 4
    for y in reversed(range(size)):
        transparent = bytearray(stride)
        for x in range(size):
            dx = (x + 0.5) * 32 / size
            dy = (y + 0.5) * 32 / size
            corner_x = max(6 - dx, dx - 26, 0)
            corner_y = max(6 - dy, dy - 26, 0)
            if corner_x ** 2 + corner_y ** 2 >= 36:
                pixels.extend((0, 0, 0, 0))
                transparent[x // 8] |= 0x80 >> (x % 8)
                continue
            if 22 <= dy < 24 and 7 <= dx < 25:
                color = (237, 215, 189, 255)
            elif ((7 <= dx < 11 and 17 <= dy < 21)
                  or (14 <= dx < 18 and 13 <= dy < 21)
                  or (21 <= dx < 25 and 8 <= dy < 21)):
                color = (255, 249, 241, 255)
            else:
                color = (126, 82, 23, 255)
            pixels.extend(color)
        mask.extend(transparent)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    return header + pixels + mask


sizes = (16, 32, 48)
frames = [icon_frame(size) for size in sizes]
directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
offset = 6 + len(frames) * 16
for size, frame in zip(sizes, frames):
    directory.extend(struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(frame), offset))
    offset += len(frame)
Path(__file__).with_name("App.ico").write_bytes(directory + b"".join(frames))
