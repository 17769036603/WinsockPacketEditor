package com.xnas.wpe.bosshunt;

import android.content.Context;
import java.util.ArrayList;
import java.util.Collections;

public final class BossMapCatalogColdStart {
  static int assertions;
  static void check(boolean ok,String message){assertions++;if(!ok)throw new AssertionError(message);}
  public static void main(String[] args){
    Context context=new Context();
    String catalog="1010:东海渔村,1002:地府,1003:北俱芦洲,1016:御马监,1018:宝象国,1060:副本第六图";
    BossHuntMapCatalog.refresh(context,catalog);
    check(BossHuntMapCatalog.MAPS.size()>5&&BossHuntMapCatalog.contains(1060),"worker catalog refresh must accept map beyond the five built-ins");
    BossHuntMapCatalog.MAPS=Collections.unmodifiableList(new ArrayList<>(BossHuntMapCatalog.MAPS.subList(0,5)));
    BossHuntMapCatalog.load(context);
    check(BossHuntMapCatalog.contains(1060),"cold load from persisted map_catalog_v1 must restore map six");
    BossHuntSelection selected=BossHuntSelection.parseCsv("1060");
    check(selected.contains(1060)&&"1060".equals(selected.toCsv()),"selection parser must accept cached sixth map after cold load");
    System.out.println("Boss dynamic map cold load: PASS ("+assertions+" assertions)");
  }
}
