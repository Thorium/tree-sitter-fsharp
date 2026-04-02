[
  (value_declaration)
  (module_defn)
  (paren_expression)
  (brace_expression)
  (anon_record_expression)
  (collection_expression)
  (loop_expression)
  (if_expression)
  (elif_expression)
  (rule)
] @indent.begin

((rules) @indent.begin
 (#set! indent.start_at_same_line))

((application_expression) @indent.align
  (#set! indent.open_delimiter "(")
  (#set! indent.close_delimiter ")"))

(paren_expression
  ")" @indent.branch)

(brace_expression
  "}" @indent.branch)

(anon_record_expression
  "|}" @indent.branch)

(collection_expression
  "]" @indent.branch)

(collection_expression
  "|]" @indent.branch)

(ERROR
  .
  [
   "module"
   "do"
  ]) @indent.begin

[
 (string)
 (line_comment)
 (xml_doc)
 (block_comment)
] @indent.auto
