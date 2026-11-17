#pragma once

#include "lan.h"

#include "Context.h"

void LAN2LLVMEmmitInstruction(LAN2LLVMContext* context, LANAstModule* module, LANAstInstr* instr);
void LAN2LLVMEmmitModule(LAN2LLVMContext* context, LANAstModule* module);