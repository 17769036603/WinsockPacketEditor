package com.xnas.wpe.bosshunt;

final class BossHuntSelection {
  final String csv; private BossHuntSelection(String v){csv=v;}
  static BossHuntSelection parseCsv(String v){return new BossHuntSelection(v);}
  boolean isEmpty(){return csv.isEmpty();} String toCsv(){return csv;}
}
final class BossHuntStatus {
  long acceptedSequence; String state="reading"; boolean cleanupConfirmed;
  boolean canStartHangup(BossHuntSelection maps,long now){return true;}
  boolean confirmsCleanupFor(long sequence){return cleanupConfirmed;}
}
final class BossHuntCommand {
  enum Mode { SCAN, RUN }
  long sequence; Mode mode;
}
