# Host API surface

Derived from 46 compiled Lua 5.0 chunks (267 prototypes).
Arity is taken from the OP_CALL that consumes each global, so it is exact for
straight-line calls and absent where the callee reached the call site indirectly.

## Native functions — called but never assigned (the port must implement these) (13)

| Name | Calls | Arity | Files |
|---|---:|---|---:|
| `Col` | 11498 | 6 | 45 |
| `Tfm` | 9532 | 7 | 45 |
| `Bbx` | 1374 | 6 | 45 |
| `Obj` | 1374 | 2 | 45 |
| `Anm` | 176 | 2 | 45 |
| `SetActorToTextMappingWithJustificationAndSizeMultiplier` | 93 | 6 | 1 |
| `SetActorToIconMapping` | 76 | 2 | 1 |
| `IncludeScript` | 45 | 1 | 1 |
| `SetActorToTextMappingWithSizeMultiplier` | 12 | 4 | 1 |
| `SetCurrentSceneTo2DMain` | 1 | 0 | 1 |
| `SetCurrentSceneToFadeToBlack` | 1 | 0 | 1 |
| `SetCurrentSceneToJumbotron` | 1 | 0 | 1 |
| `SetCurrentSceneToJumbotronBehindBuzz` | 1 | 0 | 1 |

## Script-defined functions — called and assigned in Lua (221)

| Name | Calls | Arity | Files |
|---|---:|---|---:|
| `BZ_FE_BUMPSHORT_BuzzStop` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_BuzzStop_ACT_BuzzStop_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_FastestFingerFirst` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_FastestFingerFirst_ACT_FastestFingerFirst_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_HotSeat` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_HotSeat_ACT_HotSeat_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_OffLoader` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_OffLoader_ACT_OffLoader_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_PassTheBomb` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_PassTheBomb_ACT_PassTheBomb_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_PointsBuilder` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_PointsBuilder_ACT_PointsBuilder_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_Quickfire` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_QuickfireQuiz` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_QuickfireQuiz_ACT_QuickfireQuiz_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_Quickfire_ACT_Quickfire_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_QuizMaster` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_QuizMaster_ACT_QuizMaster_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_Snap` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_Snap_ACT_Snap_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_SpeedTimeBuilder` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_SpeedTimeBuilder_ACT_SpeedTimeBuilder_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_TriggerFinger` | 1 | 0 | 1 |
| `BZ_FE_BUMPSHORT_TriggerFinger_ACT_TriggerFinger_bumpershort` | 1 | 0 | 1 |
| `BZ_FE_BUMP_BuzzStop` | 1 | 0 | 1 |
| `BZ_FE_BUMP_BuzzStop_ACT_BuzzStop_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_FastestFingerFirst` | 1 | 0 | 1 |
| `BZ_FE_BUMP_FastestFingerFirst_ACT_FastestFingerFirst_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_HotSeat` | 1 | 0 | 1 |
| `BZ_FE_BUMP_HotSeat_ACT_HotSeat_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_OffLoader` | 1 | 0 | 1 |
| `BZ_FE_BUMP_OffLoader_ACT_OffLoader_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_PassTheBomb` | 1 | 0 | 1 |
| `BZ_FE_BUMP_PassTheBomb_ACT_PassTheBomb_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_PointsBuilder` | 1 | 0 | 1 |
| `BZ_FE_BUMP_PointsBuilder_ACT_PointsBuilder_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_Quickfire` | 1 | 0 | 1 |
| `BZ_FE_BUMP_QuickfireQuiz` | 1 | 0 | 1 |
| `BZ_FE_BUMP_QuickfireQuiz_ACT_QuickfireQuiz_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_Quickfire_ACT_Quickfire_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_QuizMaster` | 1 | 0 | 1 |
| `BZ_FE_BUMP_QuizMaster_ACT_QuizMaster_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_Snap` | 1 | 0 | 1 |
| `BZ_FE_BUMP_Snap_ACT_Snap_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_SpeedTimeBuilder` | 1 | 0 | 1 |
| `BZ_FE_BUMP_SpeedTimeBuilder_ACT_SpeedTimeBuilder_bumper` | 1 | 0 | 1 |
| `BZ_FE_BUMP_TriggerFinger` | 1 | 0 | 1 |
| `BZ_FE_BUMP_TriggerFinger_ACT_TriggerFinger_bumper` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P01_1st` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P01_2nd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P01_3rd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P01_4th` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P02_1st` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P02_2nd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P02_3rd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P02_4th` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P03_1st` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P03_2nd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P03_3rd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P03_4th` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P04_1st` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P04_2nd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P04_3rd` | 1 | 0 | 1 |
| `BZ_FE_PIP_1st_2nd_3rd_4th_ACT_P04_4th` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P01_BUZZING` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P01_CLEAR` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P01_DOWN` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P01_UP` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P02_BUZZING` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P02_CLEAR` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P02_DOWN` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P02_UP` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P03_BUZZING` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P03_CLEAR` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P03_DOWN` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P03_UP` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P04_BUZZING` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P04_CLEAR` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P04_DOWN` | 1 | 0 | 1 |
| `BZ_FE_PIP_STATES_ACT_P04_UP` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P01_answer_B` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P01_answer_G` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P01_answer_O` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P01_answer_Y` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P02_answer_B` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P02_answer_G` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P02_answer_O` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P02_answer_Y` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P03_answer_B` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P03_answer_G` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P03_answer_O` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P03_answer_Y` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P04_answer_B` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P04_answer_G` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P04_answer_O` | 1 | 0 | 1 |
| `BZ_FE_PIP_answers_ACT_P04_answer_Y` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_appear_ACT_P01_cross_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_appear_ACT_P02_cross_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_appear_ACT_P03_cross_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_appear_ACT_P04_cross_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_down` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_down_ACT_P01_cross_down` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_down_ACT_P02_cross_down` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_down_ACT_P03_cross_down` | 1 | 0 | 1 |
| `BZ_FE_PIP_cross_down_ACT_P04_cross_down` | 1 | 0 | 1 |
| `BZ_FE_PIP_downarrow_flashfade` | 1 | 0 | 1 |
| `BZ_FE_PIP_downarrow_flashfade_ACT_P01_downarrow_flashfade` | 1 | 0 | 1 |
| `BZ_FE_PIP_downarrow_flashfade_ACT_P02_downarrow_flashfade` | 1 | 0 | 1 |
| `BZ_FE_PIP_downarrow_flashfade_ACT_P03_downarrow_flashfade` | 1 | 0 | 1 |
| `BZ_FE_PIP_downarrow_flashfade_ACT_P04_downarrow_flashfade` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_P02` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_P03` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_P04` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_bombpause` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_explosion` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P01_in` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_P03` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_P04` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_bombpause` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_explosion` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_in` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P02_out` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P03_P04` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P03_bombpause` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P03_explosion` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P03_in` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P03_out` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P04_bombpause` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P04_explosion` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P04_in` | 1 | 0 | 1 |
| `BZ_FE_PIP_passbomb_ACT_P04_out` | 1 | 0 | 1 |
| `BZ_FE_PIP_tick_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_tick_appear_ACT_P01_tick_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_tick_appear_ACT_P02_tick_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_tick_appear_ACT_P03_tick_appear` | 1 | 0 | 1 |
| `BZ_FE_PIP_tick_appear_ACT_P04_tick_appear` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop_ACT_BuzzStop_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop_ACT_BuzzStop_points` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop_ACT_BuzzStop_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop_ACT_BuzzStop_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_BuzzStop_ACT_BuzzStop_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst_ACT_FastestFingerFirst_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst_ACT_FastestFingerFirst_points` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst_ACT_FastestFingerFirst_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst_ACT_FastestFingerFirst_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_FastestFingerFirst_ACT_FastestFingerFirst_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat_ACT_HotSeat_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat_ACT_HotSeat_points` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat_ACT_HotSeat_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat_ACT_HotSeat_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_HotSeat_ACT_HotSeat_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader_ACT_OffLoader_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader_ACT_OffLoader_points` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader_ACT_OffLoader_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader_ACT_OffLoader_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_OffLoader_ACT_OffLoader_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb_ACT_PassTheBomb_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb_ACT_PassTheBomb_points` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb_ACT_PassTheBomb_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb_ACT_PassTheBomb_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_PassTheBomb_ACT_PassTheBomb_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_PointsBuilder` | 1 | 0 | 1 |
| `BZ_FE_RS_PointsBuilder_ACT_PointsBuilder_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_PointsBuilder_ACT_PointsBuilder_points` | 1 | 0 | 1 |
| `BZ_FE_RS_PointsBuilder_ACT_PointsBuilder_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_PointsBuilder_ACT_PointsBuilder_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz_ACT_QuickfireQuiz_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz_ACT_QuickfireQuiz_points` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz_ACT_QuickfireQuiz_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz_ACT_QuickfireQuiz_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_QuickfireQuiz_ACT_QuickfireQuiz_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire_ACT_Quickfire_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire_ACT_Quickfire_points` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire_ACT_Quickfire_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire_ACT_Quickfire_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_Quickfire_ACT_Quickfire_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster_ACT_QuizMaster_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster_ACT_QuizMaster_points` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster_ACT_QuizMaster_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster_ACT_QuizMaster_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_QuizMaster_ACT_QuizMaster_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap_ACT_Snap_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap_ACT_Snap_points` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap_ACT_Snap_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap_ACT_Snap_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_Snap_ACT_Snap_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder_ACT_SpeedTimeBuilder_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder_ACT_SpeedTimeBuilder_points` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder_ACT_SpeedTimeBuilder_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder_ACT_SpeedTimeBuilder_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_SpeedTimeBuilder_ACT_SpeedTimeBuilder_rules_03` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger_ACT_TriggerFinger_fade` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger_ACT_TriggerFinger_points` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger_ACT_TriggerFinger_rules_01` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger_ACT_TriggerFinger_rules_02` | 1 | 0 | 1 |
| `BZ_FE_RS_TriggerFinger_ACT_TriggerFinger_rules_03` | 1 | 0 | 1 |
| `BZ_FE_flyout` | 1 | 0 | 1 |
| `BZ_FE_flyout_ACT_P01_flyout_out` | 1 | 0 | 1 |
| `BZ_FE_flyout_ACT_P02_flyout_out` | 1 | 0 | 1 |
| `BZ_FE_flyout_ACT_P03_flyout_out` | 1 | 0 | 1 |
| `BZ_FE_flyout_ACT_P04_flyout_out` | 1 | 0 | 1 |
| `BZ_FE_flyout_CT_P01_flyout_up` | 1 | 0 | 1 |
| `BZ_FE_flyout_CT_P02_flyout_up` | 1 | 0 | 1 |
| `BZ_FE_flyout_CT_P03_flyout_up` | 1 | 0 | 1 |
| `BZ_FE_flyout_CT_P04_flyout_up` | 1 | 0 | 1 |

## Host constants — read but never assigned (0)

| Name | Reads | Writes | Files |
|---|---:|---:|---:|

## Script globals — assigned in Lua (game state) (0)

| Name | Reads | Writes | Files |
|---|---:|---:|---:|

## Method names (OP_SELF — object model) (0)

| Name | Calls | Arity | Files |
|---|---:|---|---:|

## Table field names (OP_GETTABLE / OP_SETTABLE constant keys) (0)

| Name | Reads | Writes | Files |
|---|---:|---:|---:|

