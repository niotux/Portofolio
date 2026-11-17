#pragma once

#define Op_Unknown 0x00

#define Op_Fun 0x01
#define Op_LdArg 0x02
#define Op_Ret 0x03

#define Op_Call 0x04
#define Op_Arg 0x05

#define Op_Set 0x06

#define Op_Add 0x07
#define Op_Sub 0x08
#define Op_Mul 0x09
#define Op_Div 0x0A
#define Op_Mod 0x0B

#define Op_IDiv 0x0C
#define Op_IMod 0x0D

#define Op_FAdd 0x0E
#define Op_FSub 0x0F
#define Op_FMul 0x10
#define Op_FDiv 0x11
#define Op_FMod 0x12

#define Op_Or 0x13
#define Op_And 0x14
#define Op_Xor 0x15
#define Op_ShL 0x16
#define Op_ShR 0x17
#define Op_IShR 0x18
#define Op_Not 0x19

#define Op_Alloc 0x1A
#define Op_Write 0x1B
#define Op_Read 0x1C
#define Op_Gep 0x1D

#define Op_Conv 0x1E
#define Op_IConv 0x1F
#define Op_FConv 0x20
#define Op_IToF 0x21
#define Op_UToF 0x22
#define Op_FToI 0x23
#define Op_FToU 0x24

#define Op_Label 0x25
#define Op_Jmp 0x26
#define Op_Jeq 0x27
#define Op_Jne 0x28
#define Op_Jlt 0x29
#define Op_Jgt 0x2A
#define Op_Jle 0x2B
#define Op_Jge 0x2C
#define Op_IJlt 0x2D
#define Op_IJgt 0x2E
#define Op_IJle 0x2F
#define Op_IJge 0x30
#define Op_FJlt 0x31
#define Op_FJgt 0x32
#define Op_FJle 0x33
#define Op_FJge 0x34