// Optional fixture regeneration: node scenarios/generate-library.mjs
import {writeFileSync} from 'node:fs';
const p=(x,y,z=0)=>({x,y,z});
const blocks=[];
for(let y=0;y<=6;y++) for(let x=0;x<=5;x++) blocks.push({position:p(x,y),type:'FLOOR'});
const scenario={
  seed:42,maxTicks:3000,blocks,
  materials:[
    {id:'wall',position:p(0,1,1),type:'WALL'},
    {id:'window',position:p(0,2,1),type:'WINDOW'},
    {id:'delivery',position:p(0,5,1),type:'PILLAR'}
  ],
  tasks:[
    {id:'wall-task',position:p(5,1,1),type:'WALL',requiredMaterial:'WALL'},
    {id:'window-task',position:p(5,2,1),type:'WINDOW',requiredMaterial:'WINDOW'}
  ],
  zones:[{id:'storage',name:'置き場1',type:'MATERIAL_STORAGE',cells:[p(4,4),p(4,5)]}],
  robots:[
    {id:'Builder',position:p(1,1),direction:'WEST',program:'../php/programs/builder.php'},
    {id:'Carrier',position:p(1,5),direction:'WEST',program:'../php/programs/carrier.php'}
  ]
};
writeFileSync(new URL('library.json',import.meta.url),JSON.stringify(scenario,null,2)+'\n');
