"""Bible crop foreground mask (numpy only, shared by render_props.py inside Blender and compose_sheets.py).

Foreground = pixels more than 90 (sum of |RGB - background|, 0..765) from the crop's corner colour, which leaves
out the soft drop shadows; enclosed holes (cream emblems, highlights) are filled; slivers of neighbouring bible items
that touch the crop border are dropped (each border-connected component under 12% of the foreground)."""
import numpy as np


def _grow(seed, allowed):
    reach = seed & allowed
    while True:
        g = reach.copy()
        g[1:, :] |= reach[:-1, :]
        g[:-1, :] |= reach[1:, :]
        g[:, 1:] |= reach[:, :-1]
        g[:, :-1] |= reach[:, 1:]
        g &= allowed
        if (g == reach).all():
            return reach
        reach = g


def fg_mask(rgb_uint8):
    a = rgb_uint8.astype(int)
    bg = a[2, 2]
    fg = np.abs(a - bg).sum(2) > 90
    border = np.zeros_like(fg)
    border[0, :] = border[-1, :] = border[:, 0] = border[:, -1] = True
    fg = ~_grow(border, ~fg)                      # fill holes
    total = fg.sum()
    todo = fg & border
    while todo.any():                             # each border-touching component on its own
        ys, xs = np.nonzero(todo)
        seed = np.zeros_like(fg)
        seed[ys[0], xs[0]] = True
        comp = _grow(seed, fg)
        if comp.sum() < 0.12 * total:
            fg &= ~comp
        todo &= ~comp
    return fg


def fg_box(rgb_uint8):
    fg = fg_mask(rgb_uint8)
    ys, xs = np.nonzero(fg)
    return xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
