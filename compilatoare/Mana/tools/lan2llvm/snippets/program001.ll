  %a = alloca i32
  store i32 %0, ptr %a
  %b = alloca i32
  store i32 %1, ptr %b
  %p = alloca ptr
  store ptr 0, ptr %p
  %r.l = load i32, ptr %a
  %r.r = load i32, ptr %b
  %r.v = add i32 %r.l, %r.r
  %r = alloca i32
  store i32 %r.v, ptr %r
  %conv.v = load i32, ptr %r
  %<f>fr.v = sitofp i32 %conv.v to double
  %fr.v = bitcast double %<f>fr.v to i64
  %fr = alloca i64
  store i64 %fr.v, ptr %fr
  %conv.v = load i32, ptr %a
  %<f>fa.v = sitofp i32 %conv.v to double
  %fa.v = bitcast double %<f>fa.v to i64
  %fa = alloca i64
  store i64 %fa.v, ptr %fa
  %conv.v = load i32, ptr %b
  %<f>fb.v = sitofp i32 %conv.v to double
  %fb.v = bitcast double %<f>fb.v to i64
  %fb = alloca i64
  store i64 %fb.v, ptr %fb
  %fm.l.bits = load i64, ptr %fa
  %fm.r.bits = load i64, ptr %fb
  %<f>fm.l = bitcast i64 %fm.l.bits to double
  %<f>fm.r = bitcast i64 %fm.r.bits to double
  %<f>fm.v = fmul double %<f>fm.l, %<f>fm.r
  %fm.v.bits = bitcast double %<f>fm.v to i64
  %fm = alloca i64
  store i64 %fm.v.bits, ptr %fm
Loop:
  %fm.l.bits = load i64, ptr %fm
  %fm.r.bits = load i64, ptr %fr
  %<f>fm.l = bitcast i64 %fm.l.bits to double
  %<f>fm.r = bitcast i64 %fm.r.bits to double
  %<f>fm.v = fdiv double %<f>fm.l, %<f>fm.r
  %fm.v.bits = bitcast double %<f>fm.v to i64
  %fm = alloca i64
  store i64 %fm.v.bits, ptr %fm
  %fjcmp.l.bits = load i64, ptr %fm
  %fjcmp.r.bits = load i64, ptr 2.0
  %<f>fjcmp.l = bitcast i64 %fjcmp.l.bits to double
  %<f>fjcmp.r = bitcast i64 %fjcmp.r.bits to double
  %fjcmp.c = fcmp olt double %<f>fjcmp.l, %<f>fjcmp.r
  br i1 %fjcmp.c, label %Loop, label %jfall.Loop
jfall.Loop:
  %ret.v = load i64, ptr %fm
  ret i64 %ret.v
