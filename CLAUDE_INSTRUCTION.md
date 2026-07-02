# Instruction for Claude Code

## Your Task

Read the comprehensive specification in `PROMPT_FOR_CLAUDE_CODE.md` and implement the solution.

## Key Directives

1. **Read the spec thoroughly** — It contains:
   - Full problem analysis with real conversation transcript
   - Root cause breakdown
   - Success criteria (7 test cases)
   - Architecture guidance

2. **Think holistically** — Don't just patch bugs. Design a proper conversational AI architecture that:
   - Eliminates hardcoded patterns
   - Uses semantic understanding (AI-driven intent classification)
   - Maintains conversation context across unlimited bubbles
   - Stays within JIFAS scope dynamically

3. **Zero tolerance for errors** — Verify:
   - All code compiles after each phase
   - No breaking changes to API contracts
   - Dependency injection properly configured
   - Null safety and error handling in place

4. **Improve beyond the spec** — If you identify additional bugs or architectural issues not mentioned in the spec, fix them proactively. Document what you found and why you fixed it.

5. **Find all bugs comprehensively** — Don't assume the spec lists everything. Analyze the codebase for:
   - Similar patterns that cause the same issue
   - Related services that need updating
   - Edge cases not covered in the spec

6. **Frontend consideration** — Frontend repo is at `D:\Users\magang.it8\JIFAS_TU\jifas-web`. If you identify issues that require frontend changes (e.g., markdown rendering for chat bubbles), document them clearly but don't modify frontend code. Focus on backend fixes.

## Implementation Guidelines

**Phase 1: Analysis**
- Read spec + analyze codebase
- Identify all affected files
- Create implementation plan

**Phase 2: Core Architecture**
- Implement intent classification system (AI-driven, not keyword-based)
- Fix pipeline order (context before decisions)
- Fix cache invalidation
- Eliminate hardcoded patterns

**Phase 3: Handlers**
- Meta-question handler (recap, history)
- Clarification handler (follow-ups)
- Response formatting cleanup

**Phase 4: Verification**
- Run all 7 test cases from spec
- Check for compilation errors
- Verify dependency injection

## Constraints

- **Minimize file reads** — Batch related operations
- **Don't break existing APIs** — Frontend depends on current response schema
- **Use existing infrastructure** — Ollama, pgvector, Redis, ChatHistory table
- **Performance budget:** Max +500ms latency acceptable for better UX

## Output Requirements

At the end, provide:

1. **Summary of changes:**
   - New files created
   - Files modified
   - Key architectural decisions

2. **Bug fixes beyond spec:**
   - What additional issues you found
   - Why they needed fixing

3. **Test results:**
   - All 7 test cases verification

4. **Frontend recommendations:**
   - If any issues require frontend changes (e.g., markdown rendering)

## Critical Success Factors

After your implementation:
- ✅ User can have 1000-bubble conversation that stays contextual
- ✅ "gajelas", "hah gimana", "tadi aku nanya apa" dan bukan hanya ini aja, semua chat akan di handle dengan baik, tanpa hardcoded agar lebih luas dan leluasa utk chatingan dengan chatbot dan tetap pada context jifas dan work naturally
- ✅ Zero false OOS rejections on valid follow-ups
- ✅ Multi-language (formal, gaul, English, campur) supported
- ✅ Clean response formatting

## Start Implementation

Begin by reading `PROMPT_FOR_CLAUDE_CODE.md` in full, then proceed with your implementation plan.

**Remember:** This is not a patch job. Build a proper conversational AI that makes the user proud to use it.
