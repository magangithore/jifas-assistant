$ErrorActionPreference = "Stop"
$kbRoot     = "D:\Users\magang.it8\jifas-assistant\Jifas.Assistant\KnowledgeBase"
$connStr    = "Server=(localdb)\MSSQLLocalDB;Database=JIFAS_Assistant;Integrated Security=true;Encrypt=false;TrustServerCertificate=true"
$ollamaUrl  = "http://10.0.12.54:11434/api/embeddings"
$embedModel = "qwen3-embedding:4b"
$chunkSize  = 500
$overlap    = 50

function Open-Conn { $c = New-Object System.Data.SqlClient.SqlConnection($connStr); $c.Open(); return $c }
function Exec-NonQuery($conn,$sql,$p){ $cmd=$conn.CreateCommand();$cmd.CommandText=$sql;$cmd.CommandTimeout=120;foreach($k in $p.Keys){$cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null};$cmd.ExecuteNonQuery()|Out-Null }
function Exec-Scalar($conn,$sql,$p){ $cmd=$conn.CreateCommand();$cmd.CommandText=$sql;$cmd.CommandTimeout=120;foreach($k in $p.Keys){$cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null};return $cmd.ExecuteScalar() }

function Get-Chunks($text){
    $list=[System.Collections.Generic.List[hashtable]]::new(); $start=0
    while($start -lt $text.Length){
        $end=[Math]::Min($start+$chunkSize,$text.Length)
        if($end -lt $text.Length){ $bp=$text.LastIndexOf(' ',$end); if($bp -gt $start){$end=$bp} }
        $c=$text.Substring($start,$end-$start).Trim()
        if($c.Length -gt 10){ $list.Add(@{Content=$c;Start=$start;End=$end}) }
        $start=[Math]::Max($start+1,$end-$overlap)
    }
    return $list
}

function Get-Embedding($text){
    $json="{`"model`":`"$embedModel`",`"prompt`":" + ($text|ConvertTo-Json) + "}"
    $r=Invoke-RestMethod -Uri $ollamaUrl -Method POST -ContentType "application/json" -Body $json
    return $r.embedding
}

function Get-Category($path){
    $p=Split-Path (Split-Path $path -Parent) -Leaf
    if($p -eq "KnowledgeBase"){return "General"}; return $p
}

Write-Host "[1/4] Clearing..." -ForegroundColor Yellow1
$conn=Open-Conn
Exec-NonQuery $conn "DELETE FROM dbo.KnowledgeBaseChunks" @{}
Exec-NonQuery $conn "DELETE FROM dbo.KnowledgeBaseDocuments" @{}
Exec-NonQuery $conn "DBCC CHECKIDENT('dbo.KnowledgeBaseDocuments',RESEED,0)" @{}
Exec-NonQuery $conn "DBCC CHECKIDENT('dbo.KnowledgeBaseChunks',RESEED,0)" @{}
$conn.Close(); Write-Host "  Cleared." -ForegroundColor Green

Write-Host "[2/4] Scanning files..." -ForegroundColor Yellow
$files=Get-ChildItem $kbRoot -Recurse -Filter "*.txt"|Sort-Object FullName
Write-Host "  Found: $($files.Count) files" -ForegroundColor Green

Write-Host "[3/4] Inserting documents..." -ForegroundColor Yellow
$docIds=@{}; $dn=0
$sqlDoc="INSERT INTO dbo.KnowledgeBaseDocuments(Title,Content,Category,Tags,IsActive,CreatedAt,UpdatedAt) OUTPUT INSERTED.Id VALUES(@T,@C,@Cat,@Tag,1,@N,@N)"
foreach($f in $files){
    $txt=Get-Content $f.FullName -Raw -Encoding UTF8
    if([string]::IsNullOrWhiteSpace($txt)){continue}
    $title=[IO.Path]::GetFileNameWithoutExtension($f.Name)
    $cat=Get-Category $f.FullName; $now=(Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $conn=Open-Conn
    $id=Exec-Scalar $conn $sqlDoc @{"@T"=$title;"@C"=$txt;"@Cat"=$cat;"@Tag"=$cat;"@N"=$now}
    $conn.Close(); $docIds[$f.FullName]=[int]$id; $dn++
    Write-Host "  [$dn/$($files.Count)] $title id=$id cat=$cat" -ForegroundColor Cyan
}
Write-Host "  $dn docs inserted." -ForegroundColor Green

Write-Host "[4/4] Chunking + embedding..." -ForegroundColor Yellow
$tc=0; $fn=0
$sqlChunk="INSERT INTO dbo.KnowledgeBaseChunks(DocumentId,ChunkIndex,Content,Embedding,EmbeddingDimensions,StartCharPos,EndCharPos,CreatedAt,UpdatedAt) VALUES(@D,@I,@C,@E,@Dim,@S,@En,@N,@N)"
foreach($f in $files){
    $txt=Get-Content $f.FullName -Raw -Encoding UTF8
    if([string]::IsNullOrWhiteSpace($txt)){continue}
    $docId=$docIds[$f.FullName]; $title=[IO.Path]::GetFileNameWithoutExtension($f.Name)
    $chunks=Get-Chunks $txt; $fn++
    Write-Host "  [$fn/$($files.Count)] $title => $($chunks.Count) chunks" -ForegroundColor White
    $ci=0
    foreach($ch in $chunks){
        try{
            $emb=Get-Embedding $ch.Content; $embJson=$emb|ConvertTo-Json -Compress; $dims=$emb.Count
            $now=(Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
            $conn=Open-Conn
            Exec-NonQuery $conn $sqlChunk @{"@D"=$docId;"@I"=$ci;"@C"=$ch.Content;"@E"=$embJson;"@Dim"=$dims;"@S"=$ch.Start;"@En"=$ch.End;"@N"=$now}
            $conn.Close(); $ci++; $tc++
            Write-Host "    chunk $ci/$($chunks.Count) ok ($dims dims)" -ForegroundColor DarkGray
        }catch{ Write-Host "    ERR chunk $ci : $_" -ForegroundColor Red }
    }
    Write-Host "  DONE $title ($ci chunks)" -ForegroundColor Green
}

$conn=Open-Conn
$fd=Exec-Scalar $conn "SELECT COUNT(*) FROM dbo.KnowledgeBaseDocuments" @{}
$fc=Exec-Scalar $conn "SELECT COUNT(*) FROM dbo.KnowledgeBaseChunks" @{}
$conn.Close()
Write-Host ""; Write-Host "==== SELESAI ====" -ForegroundColor Magenta
Write-Host "Documents : $fd" -ForegroundColor Magenta
Write-Host "Chunks    : $fc" -ForegroundColor Magenta
