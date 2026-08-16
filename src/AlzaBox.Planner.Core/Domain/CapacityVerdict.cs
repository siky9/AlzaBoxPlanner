namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// O co se plán zastavil – a hlavně jak číst hlášený odstup od horní meze
/// (<see cref="LoadPlan.GapPercent"/>).
/// </summary>
/// <remarks>
/// Výběrová fáze počítá se souhrnnou kapacitou flotily (840 m³), nakládací se 120 samostatnými
/// dodávkami po 7 m³. Pro drobné zásilky je ten rozdíl zaokrouhlovací, pro hrubou zrnitost ne –
/// a v druhém případě přestává být odstup od meze údaj o ztrátě. Program to pozná sám, aby
/// to nemusel nikdo číst z README.
/// </remarks>
public enum CapacityVerdict
{
    /// <summary>
    /// Ve skladu nezůstala žádná zásilka, kterou by tahle flotila unesla – buď se odvezlo
    /// všechno (úterý a čtvrtek), nebo je zbytek nepřepravitelný.
    /// </summary>
    /// <remarks>
    /// Záměrně <b>neříká</b> „nabídka došla“: zbýt může i sklad plný zboží přes 7 m³, které
    /// potřebuje jiné vozidlo. Kolik toho je a za kolik, hlásí
    /// <see cref="LoadPlan.NonTransportableCount"/> – odstup od meze na to neupozorní, protože
    /// mez počítá jen s tím, co flotila fyzicky uveze.
    /// </remarks>
    NothingLeftToCarry,

    /// <summary>
    /// Úzké hrdlo je vyčerpané. Odstup od meze je skutečná ztráta výběru a v praxi setiny procenta.
    /// </summary>
    Saturated,

    /// <summary>
    /// Ve flotile zbývá místo, ale nic ze skladu se do něj nevejde – limituje zrnitost nákladu,
    /// ne výběr. Horní mez bere kapacitu jako tekutinu, takže odstup ztrátu <b>nadhodnocuje</b>:
    /// plán může být optimální a mez přesto vzdálená (viz předpoklad P4 v README).
    /// </summary>
    GranularityLimited,

    /// <summary>
    /// Ve flotile zůstalo použitelné místo <i>a zároveň</i> zásilky, které by se do něj vešly.
    /// To nemá nastat – dosypávací fáze měla doběhnout, takže je to signál chyby v nakládání.
    /// <see cref="Validation.LoadPlanValidator"/> to hlásí jako problém.
    /// </summary>
    SpaceLeftUnused,
}
