var hasRisk = false;
var TROMRISK = "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/tromboserisiko";
var TROMBOSE_PATIENT_FIELDS = [
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/alder_>_60",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/aktiv_kreft_eller_pågående_kreftbehandling",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/dehydrert",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/kjent_trombofili",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/sykelig_overvekt",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/en_eller_flere_medisinsk_relevante_komorbiditeter",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/førstegradsslektning_med_tidligere_venøs_trombose",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/bruker_hormontilskudd_(hrt)",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/bruker_antikonsepsjonsmiddel_som_inneholder_østrogen",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/varicer_med_thrombophlebitt",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/pasientrelatert/gravid_eller_<_6_uker_post_partum"
];
var TROMBOSE_ADDMITTANCE_FIELDS = [
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/betydelig_redusert_mobilitet_>_3_dager",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/hofte_eller_kneoperasjon",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/hoftebrudd",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/total_operasjonstid_(anestesi_+_kirurgi)_>_90_min",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/kirurgi_på_nedre_ekstremitet_med_operasjonstid_(anestesi_+_kirurgi)_>60_min",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/akutt_kirurgisk_innleggelse_med_betennelsestilstand_eller_intra-abdominell_tilstand",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/innleggelse_på_intensivavdelingen",
    "vte_screening/screening_for_tromboserisiko/any_event/tromsboserisiko/innleggelsesrelatert/kirurgi_som_medfører_betydelig_reduksjon_i_mobilitet"
];
var ALL_TROMBO_FIELDS = TROMBOSE_ADDMITTANCE_FIELDS.concat(TROMBOSE_PATIENT_FIELDS);
var BLEEDING_PATIENT_FIELDS = [
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/aktiv_blødning",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/ervervet_blødningsforstyrrelse",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/bruk_av_antikoagulantia_med_kjent_økt_risiko_for_blødning",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/akutt_slaganfall",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/trombocytopeni",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/ukontrollert_systolisk_hypertensjon",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/pasientrelatert/ubehandlete_arvelige_blødningssykdommer",
];
var BLEEDING_ADMITTANCE_FIELDS = [
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/innleggelsesrelatert/nevrokirurgi,_spinalkirurgi_eller_øyekirurgi",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/innleggelsesrelatert/annen_prosedyre_med_høy_blødningsrisiko",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/innleggelsesrelatert/forventet_lumbal_spinal_epiduralanestesi_innen_de_neste_12_timene",
    "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/innleggelsesrelatert/lumbal_spinal_epiduralanestesi_de_siste_4_timene"
];
var BLEEDING_RISK = "vte_screening/screening_for_tromboserisiko/any_event/blødningsrisiko/blødningsrisiko";
var ALL_BLEEDING_FIELDS = BLEEDING_PATIENT_FIELDS.concat(BLEEDING_ADMITTANCE_FIELDS);
ALL_TROMBO_FIELDS.forEach(function (id) {
    api.addListener(id, "OnChanged", function (id, value, parent) {
        updateTromboseRisk(id, value);
    });
});
ALL_BLEEDING_FIELDS.forEach(function (id) {
    api.addListener(id, "OnChanged", function (id, value, parent) {
        updateBleedingRisk(id, value);
    });
});
function updateBleedingRisk(id, value) {
    var b = value;
    var dvb = new DvBoolean();
    if (b.Value) {
        dvb.Value = true;
        /*hideAllBut(id, ALL_BLEEDING_FIELDS);*/
    }
    else {
        dvb.Value = false;
        /*showAll(ALL_BLEEDING_FIELDS);*/
    }
    api.setFieldValue(BLEEDING_RISK, dvb);
}
function updateTromboseRisk(id, value) {
    var b = value;
    var dvb = new DvBoolean();
    if (b.Value) {
        console.log(id + " is true");
        hasRisk = true;
        dvb.Value = true;
        /*hideAllBut(id, ALL_TROMBO_FIELDS);*/
    }
    else {
        hasRisk = false;
        dvb.Value = false;
        /*showAll(ALL_TROMBO_FIELDS);*/
    }
    api.setFieldValue(TROMRISK, dvb);
}
function showAll(fields) {
    fields.forEach(function (id) {
        api.showField(id);
    });
}
function hideAllBut(id, fields) {
    for (var index = 0; index < fields.length; index++) {
        var element = fields[index];
        if (id == element) {
        }
        else {
            api.hideField(element);
        }
    }
}
