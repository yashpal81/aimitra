# Meeting Scribe — Release Push Discussion
**Date:** May 25, 2026  
**Time:** 10:00 AM – 11:15 AM IST  
**Location:** MS Teams — Engineering Sync  
**Facilitator:** Riya Sharma (Engineering Lead)  
**Scribe:** Karan Mehta  

---

## Attendees

| Name | Role |
|---|---|
| Riya Sharma | Engineering Lead |
| Dev Anand | Backend Developer |
| Priya Nair | QA Engineer |
| Sameer Rao | DevOps |
| Tanya Verma | Product Manager |
| Nikhil Joshi | Security Engineer |

---

## Agenda

1. Release readiness review for v1.2.0
2. Open blockers and risk assessment
3. Go / No-Go decision
4. Next steps

---

## Discussion Notes

### 1. Opening — Release Readiness

**Riya** opened the meeting by noting that the team was originally targeting a Monday release but the pipeline had flagged two intermittent test failures over the weekend. She asked the team to do a quick round-table on their respective readiness.

**Tanya** confirmed from the product side that stakeholders are expecting the release this week. "Marketing has already set up the announcement draft. We really need this out by Wednesday at the latest." She noted that two features — semantic routing and the PII masking engine — were highlighted in the announcement.

**Dev** responded that the core backend work is complete. The `SemanticRouter` service is stable and has been smoke-tested manually. However, he flagged that the `GuardrailService` integration test (`SemanticKernelOrchestratorTests`) was flaky — it passed locally but had one failure in CI yesterday. He mentioned he needed another pass to confirm root cause. *(Action noted)*

**Priya** confirmed she had completed regression on the WebChat interface and the Console app. She noted an edge case in the PII masking flow — when the Presidio server is unavailable, the fallback path returns a 200 with an empty mask rather than surfacing a meaningful error. She said it was low severity but wanted it logged before release. *(Action noted)*

---

### 2. Blockers and Risk Assessment

**Nikhil** raised a concern around the `PromptInjectionGuardrail` — during a review last week he noticed the regex pattern for detecting injection phrases was not exhaustive and could miss obfuscated variants. He had a patch ready but it hadn't been reviewed yet. He asked if it was acceptable to merge post-release or if it should be a hard gate. 

**Riya** said security patches should not be deferred past a release boundary. She asked Nikhil to put the PR up for expedited review today so it could be merged by EOD. *(Action noted)*

**Sameer** reported that the deployment pipeline to staging was green. Docker images are built, environment variables are loaded via the `EnvFileLoader` config, and rollback scripts are in place. He flagged one thing — the Presidio server (`presidio_server.py`) was not yet containerized and would still be run as a standalone process on the host. He asked whether someone should own a containerization ticket before the next release. *(Action noted for backlog)*

**Tanya** asked whether the flaky test was a release blocker. **Dev** said it was in the orchestration test suite and not on the critical path for the features shipping. He proposed marking it as a known flaky test with a tracking ticket and proceeding, subject to the test passing in the next scheduled run.

**Priya** agreed but asked that the flaky test not be suppressed — it should stay in the suite and be investigated in the next sprint. *(Action noted)*

---

### 3. Go / No-Go Discussion

**Riya** summarized the table:

| Area | Status | Notes |
|---|---|---|
| Backend / SemanticRouter | ✅ Ready | Manually verified |
| PII Masking Engine | ⚠️ Near-ready | Fallback error path needs a ticket |
| GuardrailService CI | ⚠️ Flaky | One CI failure; needs investigation |
| PromptInjection Guardrail | 🔴 Patch pending | Nikhil's PR must merge before release |
| WebChat / Console | ✅ Ready | Regression complete |
| DevOps / Pipeline | ✅ Ready | Presidio containerization deferred to backlog |

**Riya** proposed a conditional **Go** — release proceeds Wednesday morning, contingent on:
- Nikhil's guardrail PR being reviewed and merged by Tuesday EOD
- The flaky test passing in Monday's nightly run
- A tracking ticket raised for the Presidio fallback behavior

**All attendees agreed.**

---

### 4. Closing Remarks

**Tanya** asked if the release notes draft needed any engineering input. **Dev** said he would add a short technical blurb covering the routing and security architecture. *(Action noted)*

**Riya** closed by asking Sameer to schedule the production deployment window for Wednesday 6:00 AM IST to avoid peak traffic. *(Action noted)*

---

## Action Items

| # | Action | Owner | Due |
|---|---|---|---|
| 1 | Investigate flaky `SemanticKernelOrchestratorTests` CI failure; confirm root cause or raise tracking ticket | Dev Anand | Monday EOD |
| 2 | Raise ticket for Presidio server fallback returning empty mask on unavailability | Priya Nair | Monday |
| 3 | Submit `PromptInjectionGuardrail` patch PR for expedited review | Nikhil Joshi | Today EOD |
| 4 | Review and approve Nikhil's guardrail PR | Riya Sharma | Tuesday EOD |
| 5 | Add technical blurb (SemanticRouter + Security) to release notes draft | Dev Anand | Tuesday |
| 6 | Create backlog ticket: containerize `presidio_server.py` | Sameer Rao | This week |
| 7 | Schedule production deployment window — Wednesday 6:00 AM IST | Sameer Rao | Tuesday |

---

## Decisions Made

- **Conditional Go** for v1.2.0 release on Wednesday May 27, 2026
- Security guardrail patch is a **hard gate** — no release without it merged
- Flaky test remains in suite; not to be suppressed; tracked as known issue

---

*Notes compiled by Karan Mehta. Please reply with corrections by Tuesday noon.*
