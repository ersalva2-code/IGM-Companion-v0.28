#include <assert.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "exploration45.h"
#include "icon52_format.h"
int main(int argc,char**argv){assert(argc==2);char path[4096],line[2048];snprintf(path,sizeof(path),"%s/catalogo_observado.jsonl",argv[1]);FILE*f=fopen(path,"rb");assert(f);unsigned rows=0;while(fgets(line,sizeof(line),f)){unsigned long long ua;unsigned a,b,c,d;assert(sscanf(line,"{\"system_ua\":\"%16llx\",\"alliance_id_words\":[%u,%u,%u,%u]",&ua,&a,&b,&c,&d)==5);assert(ua&&a==1&&d==4);rows++;}fclose(f);assert(rows==2);snprintf(path,sizeof(path),"%s/exploracion.tsv",argv[1]);f=fopen(path,"rb");assert(f);rows=0;while(fgets(line,sizeof(line),f)){if(strstr(line,"FUTURE"))continue;Ex45Record r;assert(ex45_parse(line,&r));rows++;}fclose(f);assert(rows==3);snprintf(path,sizeof(path),"%s/iconos_alianzas/1_2_3_4.bmp",argv[1]);f=fopen(path,"rb");assert(f);static unsigned char pixels[ICON52_BYTES];assert(icon52_bmp(f,pixels));fclose(f);assert(pixels[0]==0&&pixels[1]==0&&pixels[2]==255&&pixels[3]==255);puts("PASS actual IGM 0.66 BETA 2 journal, exploration, BGRA180 readers accept Companion 0.28 exports");}
