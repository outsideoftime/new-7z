const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('@resvg/resvg-js');
const source = process.argv[2];
if (!source) throw new Error('Usage: node scripts/import-file-icons.cjs <icons/files>');
const target = path.resolve(__dirname, '../src/New7z.App/Assets/FileTypes');
const names = ['csharp','cpp','c','python','typescript','js','react','java','kotlin','go','rust','swift','ruby','php','powershell','shell','html','css','sass','vue','json','yaml','xml','markdown','text','image','audio','video','pdf','word','excel','powerpoint','archive','binary','font','config','git','docker','unity','blender','sql','log'];
fs.mkdirSync(path.join(target, 'Source'), { recursive: true });
for (const name of names) {
  const svg = fs.readFileSync(path.join(source, `${name}.svg`));
  if (!svg.toString().includes('The MIT License')) throw new Error(`Missing expected license: ${name}`);
  fs.writeFileSync(path.join(target, 'Source', `${name}.svg`), svg);
  const png = new Resvg(svg, { fitTo: { mode: 'width', value: 48 } }).render().asPng();
  fs.writeFileSync(path.join(target, `${name}.png`), png);
}
console.log(`Converted ${names.length} SVG file icons to 48px PNG.`);
