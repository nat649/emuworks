// SPDX-License-Identifier: MIT. Original rounded stroke alphabet.
// Each pair is a vertex (x: 0..6, y: 0..9); '/' lifts the pen.
// Rasterize at build time so keyboard scanning never waits for font geometry.
const fs = require('node:fs');
const paths = {
  '0':'204061684929080120', '1':'143039/1959', '2':'012040616344090969',
  '3':'002060446668492908/2444', '4':'401666/5059', '5':'60000424546668492908',
  '6':'6020010849296866542404', '7':'006039', '8':'204061634525030120/254566684929080625',
  '9':'6505250301204061684929',
  A:'09204069/1656', B:'090040616344046466684909', C:'612001084969',
  D:'09004061684909', E:'60000969/0454', F:'090060/0454', G:'6120010849696545',
  H:'0009/6069/0464', I:'0060/3039/0969', J:'006067492908', K:'0009/600469',
  L:'000969', M:'0900346069', N:'09006960', O:'204061684929080120',
  P:'09004061635404', Q:'204061684929080120/4669', R:'09004061635404/3469',
  S:'6120010325456769492908', T:'0060/3039', U:'000829496860', V:'003960',
  W:'0019335960', X:'0069/6009', Y:'003460/3439', Z:'00600969',
  '+':'0464/3137', '-':'0464', '*':'1257/5217', '/':'6009', '^':'153055',
  '(':'50121759', ')':'10525719', '=':'0363/0666', '.':'2939',
  ' ':'', '?':'012040616334/3839'
};
function raster(path, scale) {
  const width=scale*4+1, height=scale*6+1, masks=new Uint8Array(width*height);
  let a;
  for (let i=0; i<path.length;) {
    if (path[i]==='/') { a=undefined; ++i; continue; }
    const b=[0.75+Number(path[i++])*(width-2)/6, 0.75+Number(path[i++])*(height-2)/9];
    if (a) {
      const dx=b[0]-a[0], dy=b[1]-a[1], length=dx*dx+dy*dy, radius=scale===3?0.82:0.64;
      for (let y=0;y<height;++y) for(let x=0;x<width;++x) for(let sample=0;sample<4;++sample) {
        const sx=x+((sample&1)?0.75:0.25), sy=y+((sample&2)?0.75:0.25);
        const t=length?Math.max(0,Math.min(1,((sx-a[0])*dx+(sy-a[1])*dy)/length)):0;
        if ((sx-a[0]-t*dx)**2+(sy-a[1]-t*dy)**2<=radius*radius) masks[y*width+x]|=1<<sample;
      }
    }
    a=b;
  }
  return Array.from(masks, b=>(b&1)+((b>>1)&1)+((b>>2)&1)+((b>>3)&1));
}
module.exports = function generate(file) {
  const chars=Object.keys(paths).join('');
  let source='/* Generated from font.cjs, original EmuWorks glyphs, MIT. */\n';
  source+='static const char smooth_chars[] = '+JSON.stringify(chars)+';\n';
  for(const scale of [2,3]) {
    source+='static const unsigned char smooth_'+scale+'['+chars.length+']['+((scale*4+1)*(scale*6+1))+'] = {\n';
    for(const c of chars) source+='{'+raster(paths[c],scale).join(',')+'},\n';
    source+='};\n';
  }
  fs.writeFileSync(file,source);
};
