target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-linux-gnu"

define double @myfunc(i32 %0, i32 %1) {
  %fm = alloca i64
  %a = add i32 %0, 0
  %b = add i32 %1, 0
  %p = inttoptr i64 0 to ptr
  %r = add i32 %a, %b
  %"<f>fr" = sitofp i32 %r to double
  %"<f>fa" = sitofp i32 %a to double
  %"<f>fb" = sitofp i32 %b to double
  %"<f>fm.tmp" = fmul double %"<f>fa", %"<f>fb"
  %fm.bits.tmp = bitcast double %"<f>fm.tmp" to i64
  store i64 %fm.bits.tmp, ptr %fm
  br label %Loop
Loop:
  %fm.bits = load i64, ptr %fm
  %"<f>fm" = bitcast i64 %fm.bits to double
  %"<f>fm.tmp" = fdiv double %"<f>fm", %"<f>fr"
  %fm.bits.tmp = bitcast double %"<f>fm.tmp" to i64
  store i64 %fm.bits.tmp, ptr %fm
  %fm.bits = load i64, ptr %fm
  %"<f>fm" = bitcast i64 %fm.bits to double
  %fjcmp.c = fcmp ogt double %"<f>fm", 2.0
  br i1 %fjcmp.c, label %Loop, label %Loop.after
Loop.after:
  %fm.bits = load i64, ptr %fm
  %"<f>fm" = bitcast i64 %fm.bits to double
  ret double %"<f>fm"

}