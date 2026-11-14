import sys
from PIL import Image, ImageFilter
import numpy as np
from scipy import ndimage
src,dst=sys.argv[1],sys.argv[2]
im=Image.open(src).convert('RGB'); a=np.asarray(im).astype(int)
bg=np.array([205,205,205]); d=np.abs(a-bg).max(axis=2)
cand=d<=14
lab,n=ndimage.label(cand)
border=set(np.unique(np.concatenate([lab[0],lab[-1],lab[:,0],lab[:,-1]])))-{0}
mask_bg=np.isin(lab,list(border))
fg=~mask_bg
fg=ndimage.binary_opening(fg,iterations=1)
# keep largest components (>0.2% area)
lab2,n2=ndimage.label(fg); sizes=ndimage.sum(fg,lab2,range(1,n2+1))
keep=[i+1 for i,s in enumerate(sizes) if s>0.002*fg.size]
fg=np.isin(lab2,keep); fg=ndimage.binary_fill_holes(fg) if False else fg
alpha=Image.fromarray((fg*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
out=im.copy(); out.putalpha(alpha)
ys,xs=np.where(fg); print(dst,'fg bbox',xs.min(),ys.min(),xs.max(),ys.max(),'frac',round(fg.mean(),3))
out.save(dst)
