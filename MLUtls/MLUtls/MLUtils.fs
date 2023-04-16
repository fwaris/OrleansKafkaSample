namespace MLUtils 
open System
open Microsoft.ML
open Microsoft.ML.AutoML
open Microsoft.ML.Data
open System.Collections.Generic

module ColInfo =
    ///display all columns by each bin type (e.g. Categorical, Numeric, ...) 
    let showCols (colInfo:ColumnInformation) =
        printfn "Column Info"
        printfn "==========="
        printfn $"Categorical: %A{Seq.toArray colInfo.CategoricalColumnNames}"
        printfn $"Numeric:  %A{Seq.toArray colInfo.NumericColumnNames}"
        printfn $"Text:  %A{Seq.toArray colInfo.TextColumnNames}"
        printfn $"Label: %A{colInfo.LabelColumnName}"
        printfn $"Ignored: %A{Seq.toArray colInfo.IgnoredColumnNames}"

    ///remove a column
    let removeCol (col:string) (colInfo:ColumnInformation) =
        colInfo.CategoricalColumnNames.Remove col |> ignore
        colInfo.TextColumnNames.Remove col |> ignore
        colInfo.NumericColumnNames.Remove col |> ignore
        colInfo.IgnoredColumnNames.Remove col |> ignore
        colInfo.ImagePathColumnNames.Remove col |> ignore

    ///put the column in the 'ignore' bin (and remove it from all others)
    let ignore (cols:string seq) (colInfo:ColumnInformation) =
        cols |> Seq.iter (fun c -> removeCol c colInfo)
        cols |> Seq.iter (fun c -> colInfo.IgnoredColumnNames.Add c)

    ///put the column in the 'text' bin (and remove it from all others)
    let setAsText (cols:string seq) (colInfo:ColumnInformation) =
        cols |> Seq.iter (fun c -> removeCol c colInfo)
        cols |> Seq.iter (fun c -> colInfo.TextColumnNames.Add c)

    ///put the column in the 'categorical' bin (and remove from all others)
    let setAsCategorical (cols:string seq) (colInfo:ColumnInformation) =
        cols |> Seq.iter (fun c -> removeCol c colInfo)
        cols |> Seq.iter (fun c -> colInfo.CategoricalColumnNames.Add c)

    ///put the column in the 'numeric' bin (and remove from all others)
    let setAsNumeric (cols:string seq) (colInfo:ColumnInformation) =
        cols |> Seq.iter (fun c -> removeCol c colInfo)
        cols |> Seq.iter (fun c -> colInfo.NumericColumnNames.Add c)

module Schema =
    open System.Collections.Generic

    ///print the given schema
    let printSchema (sch:DataViewSchema) = sch |> Seq.iter (printfn "%A")

    let internal printIndent indent = for _ in 1 .. indent do  printf " "

    ///show the differences between two dataview schemas
    let diffSchemas indent (fromS:DataViewSchema) (toS:DataViewSchema) =
        let fs = fromS |> Seq.map(fun x -> x.Name, string x.Type) |> set        
        let ts = toS |> Seq.map (fun x->x.Name, string x.Type)
        let diff = ts |> Seq.filter (fs.Contains>>not) |> Seq.toList
        diff |> List.iter (fun x -> printIndent indent; printfn "%A" x)

    ///print out the transformer chain rooted at the given ITransformer
    let rec printTxChain inputSchema indent (root:ITransformer) =
        match root with
        | :? TransformerChain<ITransformer> as tx  -> 
            (inputSchema,tx) 
            ||> Seq.fold(fun inpSch tx ->
                let outSch = tx.GetOutputSchema(inpSch)
                printTxChain inpSch (indent + 1) tx
                outSch)
            |> ignore
        | _ -> 
            printIndent indent
            root.GetType().Name |> printfn "%s"
            let outSch = root.GetOutputSchema (inputSchema)
            diffSchemas (indent + 1) inputSchema outSch

    ///show the slot names associated with the given column
    let slotNames  (col:string) (schema:DataViewSchema)= 
        let mutable vbuffer = new VBuffer<System.ReadOnlyMemory<char>>()
        schema.[col].GetSlotNames(&vbuffer)
        vbuffer.DenseValues() |> Seq.map string |> Seq.toArray

    ///Create a schema definition from the given type after removing any internal fields that end with "@".
    //Note: This function is mostly used when creating a dataview from an seq of F# records
    let cleanSchema (t:Type) = 
        let sch = SchemaDefinition.Create(t)
        let atCols = sch |> Seq.filter(fun x->x.ColumnName.EndsWith("@")) |> Seq.toList
        atCols |> List.iter (sch.Remove>>ignore)        
        sch

(*
type Viz =
    static member show (dv:IDataView,?title,?rows, ?showHidden) =
        let showHidden = showHidden |> Option.defaultValue false
        let rows = defaultArg rows 1000
        let title = defaultArg title ""
        let rs = dv.Preview(rows).RowView
        let dt = new System.Data.DataTable()
        let _,idx = 
            ((Map.empty,[]),dv.Schema)
            ||> Seq.fold(fun (accMap,accIdx) field -> 
                let n = field.Name
                let accMap,accIdx =
                    match field.IsHidden,showHidden with
                    | true,true 
                    | false,_ ->
                        let acc = accMap |> Map.tryFind n |> Option.map (fun x -> accMap |> Map.add n (x+1)) |> Option.defaultWith(fun _ -> accMap |> Map.add n 0)
                        let count = acc.[n]
                        let fn = if count = 0 then n else $"{n}_{count}"
                        dt.Columns.Add(fn) |> ignore
                        acc,field.Index::accIdx
                    | _ -> accMap,accIdx
                accMap,accIdx)
        let idxSet = HashSet idx
        rs |> Seq.iter (fun r -> 
            let rowVals = r.Values |> Seq.mapi(fun i x -> i,x) |> Seq.choose (fun (i,x) -> if idxSet.Contains i then Some x.Value else None) |> Seq.toArray
            dt.Rows.Add(rowVals) |> ignore)
        let f = new System.Windows.Forms.Form()
        f.Text <- title
        let grid = new System.Windows.Forms.DataGridView()
        grid.Dock <- System.Windows.Forms.DockStyle.Fill
        grid.AutoGenerateColumns <- true
        f.Controls.Add(grid)
        f.Show()
        grid.DataSource <- dt
        grid.Columns 
        |> Seq.cast<System.Windows.Forms.DataGridViewColumn> 
        |> Seq.iter (fun c -> c.SortMode <- System.Windows.Forms.DataGridViewColumnSortMode.Automatic)

*)
//taken from ML.Net fsharp examples
module Pipeline = 
    let asEstimator (pipeline : IEstimator<'a>) =
        match pipeline with
        | :? IEstimator<ITransformer> as p -> p
        | _ -> failwith "The pipeline has to be an instance of IEstimator<ITransformer>."

    let append (estimator : IEstimator<'b>) (pipeline : IEstimator<ITransformer>)  = 
           pipeline.Append estimator

    /// combine two transformers
    let inline (<!>) b a = append (asEstimator a) (asEstimator b)  |> asEstimator
