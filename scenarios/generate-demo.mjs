// Run with `node scenarios/generate-demo.mjs` to regenerate the checked-in fixture.
import { writeFileSync } from 'node:fs';
const p = (x,y,z) => ({x,y,z});
const blocks = [];
for (let y=0;y<=5;y++) for (let x=0;x<=6;x++) {
  if (x===5 && y===4) continue;
  blocks.push({position:p(x,y,0), type:'FLOOR'});
}
blocks.push({position:p(2,0,1),type:'STAIRS',direction:'EAST'}, {position:p(3,0,1),type:'FLOOR'}, {position:p(5,2,3),type:'FLOOR'});
const types = ['WALL','FLOOR','PILLAR','WINDOW','STAIRS'];
const targets = [p(5,2,1),p(5,4,0),p(5,3,1),p(5,4,1),p(5,1,1)];
const scenario = {
  seed:42,maxHeight:4,maxTicks:2000,blocks,
  materials: types.map((type,i)=>({id:`material-${i+1}`,position:p(0,i+1,1),type,units:1})),
  tasks: types.map((type,i)=>({id:`task-${i+1}`,position:targets[i],type,requiredMaterial:type,requiredUnits:1,direction:'EAST'})),
  zones:[{id:'storage-1',name:'置き場1',type:'MATERIAL_STORAGE',cells:[p(3,4,0),p(3,5,0)]}],
  robots:[{id:'Robot01',position:p(1,1,0),direction:'WEST',program:'../php/programs/demo.php'},
          {id:'Robot02',position:p(6,5,0),direction:'NORTH',program:'../php/programs/observer.php'}]
};
writeFileSync(new URL('demo.json',import.meta.url),JSON.stringify(scenario,null,2)+'\n');
